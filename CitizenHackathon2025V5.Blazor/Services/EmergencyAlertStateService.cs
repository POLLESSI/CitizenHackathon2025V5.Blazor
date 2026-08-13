using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class EmergencyAlertStateService : IAsyncDisposable
    {
        private readonly EmergencyAlertClientService _hub;
        private readonly HttpClient _api;
        private readonly IHubTokenService _hubTokenService;
        private readonly IConfiguration _configuration;
        private readonly SemaphoreSlim _reconcileGate = new(1, 1);
        private readonly object _stateLock = new();

        private readonly Dictionary<Guid, EmergencyAlertSignalRDTO> _active = new();

        /*
         * While a REST snapshot is being fetched,
         * SignalR events are queued and replayed
         * afterwards.
         *
         * This closes the race:
         *
         * SubscribeAll
         *      ↓
         * GET active
         *      ↓
         * event arrives during GET
         *      ↓
         * queue
         *      ↓
         * snapshot
         *      ↓
         * replay event
         */
        private readonly Queue<PendingDelta> _pending = new();

        private bool _reconciling;
        private bool _started;
        private bool _handlersAttached;

        private CancellationTokenSource? _reconcileDebounceCts;

        public event Func<Task>? StateChanged;

        public EmergencyAlertStateService(EmergencyAlertClientService hub, IHttpClientFactory httpClientFactory, IHubTokenService hubTokenService, IConfiguration configuration)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));

            ArgumentNullException.ThrowIfNull(httpClientFactory);

            _api = httpClientFactory.CreateClient("ApiWithAuth");
            _hubTokenService = hubTokenService ?? throw new ArgumentNullException(nameof(hubTokenService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public IReadOnlyList<EmergencyAlertSignalRDTO> Snapshot
        {
            get
            {
                lock (_stateLock)
                {
                    return _active
                        .Values
                        .OrderByDescending(x => x.Severity)
                        .ThenByDescending(x => x.LastUpdatedAtUtc)
                        .ToArray();
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_stateLock)
                {
                    return _active.Count;
                }
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_started)
                return;

            AttachHandlers();

            /*
             * IMPORTANT:
             *
             * Connect / SubscribeAll FIRST.
             *
             * Then load REST.
             *
             * Any SignalR event arriving during
             * the REST request is queued.
             */
            await _hub.StartAsync(BuildHubUrl(), () => _hubTokenService.GetHubTokenAsync(), cancellationToken);

            await ReconcileAsync(cancellationToken);

            _started = true;

            Console.WriteLine($"[EmergencyAlertState] STARTED. " + $"Active={Count}");
        }

        public async Task ReconcileAsync(CancellationToken cancellationToken = default)
        {
            await _reconcileGate.WaitAsync(cancellationToken);

            try
            {
                lock (_stateLock)
                {
                    _reconciling = true;
                }

                Console.WriteLine("[EmergencyAlertState] " + "GET EmergencyAlerts/active");

                var snapshot = await _api.GetFromJsonAsync<List<EmergencyAlertSignalRDTO>>("EmergencyAlerts/active", cancellationToken) ?? new List<EmergencyAlertSignalRDTO>();

                lock (_stateLock)
                {
                    /*
                     * REST is authoritative.
                     */
                    _active.Clear();

                    foreach (var alert in snapshot)
                    {
                        _active[alert.Id] = alert;
                    }

                    /*
                     * Apply any real-time events
                     * received while REST was loading.
                     */
                    while (_pending.Count > 0)
                    {
                        ApplyDeltaUnsafe( _pending.Dequeue());
                    }

                    _reconciling = false;
                }

                Console.WriteLine($"[EmergencyAlertState] " + $"REST reconciled. Active={Count}");

                await RaiseStateChangedAsync();
            }
            catch
            {
                /*
                 * Do not lose SignalR events merely
                 * because REST temporarily failed.
                 */
                lock (_stateLock)
                {
                    while (_pending.Count > 0)
                    {
                        ApplyDeltaUnsafe(_pending.Dequeue());
                    }

                    _reconciling = false;
                }

                await RaiseStateChangedAsync();

                throw;
            }
            finally
            {
                _reconcileGate.Release();
            }
        }

        private void AttachHandlers()
        {
            if (_handlersAttached)
                return;

            _hub.AlertUpserted += OnAlertUpsertedAsync;

            _hub.AlertCancelled += OnAlertCancelledAsync;

            _hub.AlertExpired += OnAlertExpiredAsync;

            _hub.AlertsRefreshed += OnAlertsRefreshedAsync;

            _hub.ConnectionRestored += OnConnectionRestoredAsync;

            _handlersAttached = true;
        }

        private async Task OnAlertUpsertedAsync(EmergencyAlertSignalRDTO alert)
        {
            Console.WriteLine($"[EmergencyAlertState] UPSERT " + $"{alert.SourceCode}/" + $"{alert.ExternalId}");

            var appliedImmediately = QueueOrApply(new UpsertDelta(alert));

            if (appliedImmediately)
            {
                await RaiseStateChangedAsync();
            }

            /*
             * Reconcile shortly afterwards.
             *
             * This is important for a CAP Update:
             * B may supersede A without sending
             * CANCELLED(A).
             */
            ScheduleReconcile();
        }

        private async Task OnAlertCancelledAsync(Guid alertId, string sourceCode, string externalId)
        {
            Console.WriteLine($"[EmergencyAlertState] CANCEL " + $"{sourceCode}/{externalId}");

            var appliedImmediately = QueueOrApply(new RemoveDelta(alertId));

            if (appliedImmediately)
            {
                await RaiseStateChangedAsync();
            }

            ScheduleReconcile();
        }

        private async Task OnAlertExpiredAsync(Guid alertId, string sourceCode, string externalId)
        {
            Console.WriteLine($"[EmergencyAlertState] EXPIRE " + $"{sourceCode}/{externalId}");

            var appliedImmediately = QueueOrApply(new RemoveDelta(alertId));

            if (appliedImmediately)
            {
                await RaiseStateChangedAsync();
            }


            ScheduleReconcile();
        }

        private Task OnAlertsRefreshedAsync(EmergencyAlertRefreshDTO _)
        {
            return ReconcileAsync();
        }

        private Task OnConnectionRestoredAsync()
        {
            Console.WriteLine($"[EmergencyAlertState] Connection restored -> REST reconcile.");

            return ReconcileAsync();
        }

        private bool QueueOrApply(PendingDelta delta)
        {
            lock (_stateLock)
            {
                if (_reconciling)
                {
                    _pending.Enqueue(delta);

                    return false;
                }

                ApplyDeltaUnsafe(delta);

                return true;
            }
        }

        private void ApplyDeltaUnsafe(PendingDelta delta)
        {
            switch (delta)
            {
                case UpsertDelta upsert: _active[upsert.Alert.Id] = upsert.Alert;
                    break;

                case RemoveDelta remove: _active.Remove(remove.AlertId);
                    break;
            }
        }


        private void ScheduleReconcile()
        {
            var next = new CancellationTokenSource();

            var previous = Interlocked.Exchange(ref _reconcileDebounceCts, next);

            if (previous is not null)
            {
                try
                {
                    previous.Cancel();
                }
                catch
                {
                }

                previous.Dispose();
            }

            _ = ReconcileAfterDelayAsync(next);
        }

        private async Task ReconcileAfterDelayAsync(CancellationTokenSource cts)
        {
            try
            {
                /*
                 * Coalesce a burst such as:
                 *
                 * A Upsert
                 * B Upsert
                 * B Cancel
                 *
                 * into one authoritative REST refresh.
                 */
                await Task.Delay(300, cts.Token);


                await ReconcileAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                /*
                 * A newer SignalR event replaced
                 * this scheduled reconciliation.
                 */
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[EmergencyAlertState] " + $"Reconcile failed: {ex.Message}");
            }
        }

        private async Task RaiseStateChangedAsync()
        {
            var handlers = StateChanged;

            if (handlers is null)
                return;


            foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
            {
                await handler();
            }
        }

        private string BuildHubUrl()
        {
            var baseUrl =
                (
                    _configuration["SignalR:HubBase"]
                    ??
                    _configuration["ApiBaseUrl"]
                    ??
                    "https://localhost:7254"
                )
                .TrimEnd('/');

            var path = EmergencyAlertHubMethods.HubPath.Trim();

            if (!path.StartsWith('/'))
            {
                path = "/" + path;
            }


            if (!path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
            {
                path = "/hubs" + path;
            }

            return $"{baseUrl}{path}";
        }


        public ValueTask DisposeAsync()
        {
            if (_handlersAttached)
            {
                _hub.AlertUpserted -= OnAlertUpsertedAsync;
                _hub.AlertCancelled -= OnAlertCancelledAsync;
                _hub.AlertExpired -= OnAlertExpiredAsync;
                _hub.AlertsRefreshed -= OnAlertsRefreshedAsync;
                _hub.ConnectionRestored -= OnConnectionRestoredAsync;
                _handlersAttached = false;
            }

            var cts = Interlocked.Exchange(ref _reconcileDebounceCts, null);

            if (cts is not null)
            {
                try
                {
                    cts.Cancel();
                }
                catch
                {
                }

                cts.Dispose();
            }

            return ValueTask.CompletedTask;
        }

        private abstract record PendingDelta;

        private sealed record UpsertDelta(EmergencyAlertSignalRDTO Alert) : PendingDelta;

        private sealed record RemoveDelta(Guid AlertId) : PendingDelta;
    }
}
































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/