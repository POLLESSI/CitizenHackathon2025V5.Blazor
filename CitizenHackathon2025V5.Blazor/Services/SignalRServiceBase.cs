using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    /// <summary>
    /// Base service to manage a SignalR connection to a single hub (chosen by enum).
    /// - Centralizes the routes
    /// - Manages AccessTokenProvider
    /// - Provides RegisterHandler/Invoke/Send
    /// - Expose EnsureConnectedAsync with lightweight backoff
    /// </summary>
    public abstract class SignalRServiceBase : IAsyncDisposable
    {
        protected HubConnection? _hubConnection;
        private readonly string _baseHubUrl;
        private readonly Func<Task<string?>>? _accessTokenProviderAsync;

        // To diagnose
        public HubName? CurrentHub { get; private set; }
        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        /// <param name="baseHubUrl">
        /// API base, eg: "https://localhost:7254"
        /// </param>
        /// <param name="accessTokenProviderAsync">
        /// Optional AccessToken (JWT) provider (eg: () => Task.FromResult(token)).
        /// If null or string.IsNullOrEmpty, the connection will be made without a token.
        /// </param>
        protected SignalRServiceBase(string baseHubUrl, Func<Task<string?>>? accessTokenProviderAsync = null)
        {
            _baseHubUrl = baseHubUrl.TrimEnd('/');
            _accessTokenProviderAsync = accessTokenProviderAsync;
        }

        /// <summary>
        /// Initializes and starts the connection to the requested hub.
        /// </summary>
        protected async Task InitializeAsync(CitizenHackathon2025V5.Blazor.Client.SignalR.HubName hub, CancellationToken ct = default)
        {
            CurrentHub = hub;
            var path = CitizenHackathon2025V5.Blazor.Client.SignalR.HubRoutes.GetPath(hub);
            var fullUrl = $"{_baseHubUrl}{path}";

            // Prepare AccessTokenProvider if provided
            Func<Task<string>>? tokenFactory = null;
            if (_accessTokenProviderAsync != null)
            {
                tokenFactory = async () =>
                {
                    var t = await _accessTokenProviderAsync().ConfigureAwait(false);
                    return t ?? string.Empty;
                };
            }

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(fullUrl, options =>
                {
                    if (tokenFactory != null)
                        options.AccessTokenProvider = tokenFactory;
                })
                .WithAutomaticReconnect() // default backoff (0, 2, 10, 30 sec)
                .Build();

            // Reconnection logs
            _hubConnection.Closed += async (ex) =>
            {
                Console.WriteLine($"[SignalR] CLOSED ({hub}) - {ex?.Message}");
                // No infinite loop here, let WithAutomaticReconnect handle it,
                // or let the calling service decide whether to call EnsureConnectedAsync() again.
                await Task.CompletedTask;
            };

            _hubConnection.Reconnecting += (ex) =>
            {
                Console.WriteLine($"[SignalR] RECONNECTING ({hub}) - {ex?.Message}");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += (connId) =>
            {
                Console.WriteLine($"[SignalR] RECONNECTED ({hub}) - ConnectionId={connId}");
                return Task.CompletedTask;
            };

            await _hubConnection.StartAsync(ct).ConfigureAwait(false);
            Console.WriteLine($"[SignalR] CONNECTED to {fullUrl}");
        }

        /// <summary>
        /// Ensures the connection is active (limited attempts with small backoff).
        /// </summary>
        protected async Task EnsureConnectedAsync(int maxAttempts = 3, CancellationToken ct = default)
        {
            if (_hubConnection == null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");

            if (IsConnected) return;

            int attempt = 0;
            while (!IsConnected && attempt < maxAttempts)
            {
                attempt++;
                try
                {
                    await _hubConnection.StartAsync(ct).ConfigureAwait(false);
                    if (IsConnected) return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SignalR] EnsureConnected attempt {attempt} failed: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * attempt, 5)), ct).ConfigureAwait(false);
            }

            if (!IsConnected)
                throw new InvalidOperationException("Unable to (re)connect to SignalR hub.");
        }

        /// <summary>
        /// Registers a handler for a server method returning a payload T.
        /// </summary>
        protected void RegisterHandler<T>(string methodName, Action<T> handler)
        {
            if (_hubConnection == null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");

            _hubConnection.On(methodName, handler);
            Console.WriteLine($"[SignalR] Handler registered: {methodName} ({typeof(T).Name})");
        }

        /// <summary>
        /// Registers a handler for a server method without payload.
        /// </summary>
        protected void RegisterHandler(string methodName, Action handler)
        {
            if (_hubConnection == null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");

            _hubConnection.On(methodName, handler);
            Console.WriteLine($"[SignalR] Handler registered: {methodName} (no payload)");
        }

        /// <summary>
        /// Calls a hub-side method that returns a value.
        /// </summary>
        protected Task<TResult> InvokeAsync<TResult>(string methodName, object? arg1 = null, CancellationToken ct = default)
        {
            if (_hubConnection == null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");
            return arg1 is null
                ? _hubConnection.InvokeAsync<TResult>(methodName, cancellationToken: ct)
                : _hubConnection.InvokeAsync<TResult>(methodName, arg1, ct);
        }

        /// <summary>
        /// Calls a hub-side method (fire & forget).
        /// </summary>
        protected Task SendAsync(string methodName, object? arg1 = null, CancellationToken ct = default)
        {
            if (_hubConnection == null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");
            return arg1 is null
                ? _hubConnection.SendAsync(methodName, cancellationToken: ct)
                : _hubConnection.SendAsync(methodName, arg1, ct);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_hubConnection != null)
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                    Console.WriteLine($"[SignalR] DISPOSED ({CurrentHub})");
                }
            }
            catch { /* swallow has it */ }
        }
    }
}










































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.