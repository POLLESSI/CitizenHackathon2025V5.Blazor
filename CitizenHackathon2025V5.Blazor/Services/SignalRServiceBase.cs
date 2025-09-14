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
    /// - Exposes EnsureConnectedAsync with lightweight backoff
    /// </summary>
    public abstract class SignalRServiceBase : IAsyncDisposable
    {
        protected HubConnection? _hubConnection;
        private readonly string _baseHubUrl;
        private readonly Func<Task<string?>>? _accessTokenProviderAsync;

        // Diagnostics
        public HubName? CurrentHub { get; private set; }
        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        /// <param name="baseHubUrl">API base, eg: "https://localhost:7254"</param>
        /// <param name="accessTokenProviderAsync">
        /// Optional AccessToken (JWT) provider (eg: () => Task.FromResult(token)).
        /// If null or empty, connection is made without a token.
        /// </param>
        protected SignalRServiceBase(string baseHubUrl, Func<Task<string?>>? accessTokenProviderAsync = null)
        {
            _baseHubUrl = baseHubUrl.TrimEnd('/');
            _accessTokenProviderAsync = accessTokenProviderAsync;
        }

        /// <summary>Initializes and starts the connection to the requested hub.</summary>
        protected async Task InitializeAsync(HubName hub, CancellationToken ct = default)
        {
            CurrentHub = hub;
            var fullUrl = BuildFullUrl(hub);

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(fullUrl, options =>
                {
                    if (_accessTokenProviderAsync is not null)
                    {
                        // bridge string? -> string for AccessTokenProvider
                        options.AccessTokenProvider = async () =>
                            (await _accessTokenProviderAsync().ConfigureAwait(false)) ?? string.Empty;
                    }
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.Closed += ex =>
            {
                Console.WriteLine($"[SignalR] CLOSED ({hub}) - {ex?.Message}");
                return Task.CompletedTask;
            };
            _hubConnection.Reconnecting += ex =>
            {
                Console.WriteLine($"[SignalR] RECONNECTING ({hub}) - {ex?.Message}");
                return Task.CompletedTask;
            };
            _hubConnection.Reconnected += connId =>
            {
                Console.WriteLine($"[SignalR] RECONNECTED ({hub}) - ConnectionId={connId}");
                return Task.CompletedTask;
            };

            await _hubConnection.StartAsync(ct).ConfigureAwait(false);
            Console.WriteLine($"[SignalR] CONNECTED to {fullUrl}");
        }

        /// <summary>Constructs the full hub URL. Overridden by subclasses if needed.</summary>
        protected virtual string BuildFullUrl(HubName hub)
        {
            var path = HubRoutes.GetPath(hub);
            return $"{_baseHubUrl}{path}";
        }

        /// <summary>Ensures the connection is active (limited attempts with small backoff).</summary>
        protected async Task EnsureConnectedAsync(int maxAttempts = 3, CancellationToken ct = default)
        {
            if (_hubConnection is null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");

            if (IsConnected) return;

            for (int attempt = 1; attempt <= maxAttempts && !IsConnected; attempt++)
            {
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

        // ---------------------------
        // Handlers
        // ---------------------------

        /// <summary>Registers a handler for a server method returning a payload T (sync).</summary>
        protected void RegisterHandler<T>(string methodName, Action<T> handler)
        {
            if (_hubConnection is null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");

            _hubConnection.On(methodName, handler);
            Console.WriteLine($"[SignalR] Handler registered: {methodName} ({typeof(T).Name})");
        }

        /// <summary>Registers a handler for a server method returning a payload T (async).</summary>
        protected void RegisterHandler<T>(string methodName, Func<T, Task> handler)
        {
            if (_hubConnection is null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");

            _hubConnection.On(methodName, handler);
            Console.WriteLine($"[SignalR] Handler registered: {methodName} async ({typeof(T).Name})");
        }

        /// <summary>Registers a handler for a server method without payload (sync).</summary>
        protected void RegisterHandler(string methodName, Action handler)
        {
            if (_hubConnection is null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");

            _hubConnection.On(methodName, handler);
            Console.WriteLine($"[SignalR] Handler registered: {methodName} (no payload)");
        }

        /// <summary>Registers a handler for a server method without payload (async).</summary>
        protected void RegisterHandler(string methodName, Func<Task> handler)
        {
            if (_hubConnection is null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");

            _hubConnection.On(methodName, handler);
            Console.WriteLine($"[SignalR] Handler registered: {methodName} async (no payload)");
        }

        // ---------------------------
        // Invoke / Send
        // ---------------------------

        /// <summary>Calls a hub-side method that returns a value.</summary>
        protected Task<TResult> InvokeAsync<TResult>(string methodName, object? arg1 = null, CancellationToken ct = default)
        {
            if (_hubConnection is null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");

            return arg1 is null
                ? _hubConnection.InvokeCoreAsync<TResult>(methodName, Array.Empty<object?>(), ct)
                : _hubConnection.InvokeCoreAsync<TResult>(methodName, new object?[] { arg1 }, ct);
        }

        /// <summary>Calls a hub-side method (fire & forget).</summary>
        protected Task SendAsync(string methodName, object? arg1 = null, CancellationToken ct = default)
        {
            if (_hubConnection is null)
                throw new InvalidOperationException("HubConnection not initialized. Call InitializeAsync(...) first.");

            return arg1 is null
                ? _hubConnection.SendAsync(methodName, Array.Empty<object?>(), ct)
                : _hubConnection.SendAsync(methodName, new object?[] { arg1 }, ct);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_hubConnection is not null)
                {
                    await _hubConnection.StopAsync().ConfigureAwait(false);
                    await _hubConnection.DisposeAsync().ConfigureAwait(false);
                    Console.WriteLine($"[SignalR] DISPOSED ({CurrentHub})");
                }
            }
            catch
            {
                // swallow
            }
        }
    }
}










































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




