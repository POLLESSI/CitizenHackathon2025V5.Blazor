using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using CitizenHackathon2025V5.Blazor.Client.SignalR;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    /// <summary>
    /// Manages multiple SignalR connections (one per HubName).
    /// - Centralizes creation and reconnection
    /// - Allows RegisterHandler/Invoke/Send per hub
    /// - Supports a shared AccessTokenProvider (JWT)
    /// </summary>
    public sealed class MultiHubSignalRClient : IAsyncDisposable
    {
        private readonly string _baseHubUrl;
        private readonly Func<Task<string?>>? _accessTokenProviderAsync;

        private readonly ConcurrentDictionary<HubName, HubConnection> _connections = new();
        private readonly ConcurrentDictionary<HubName, SemaphoreSlim> _locks = new();

        public MultiHubSignalRClient(string baseHubUrl, Func<Task<string?>>? accessTokenProviderAsync = null)
        {
            _baseHubUrl = baseHubUrl.TrimEnd('/');
            _accessTokenProviderAsync = accessTokenProviderAsync;
        }

        public HubConnectionState GetState(HubName hub) =>
            _connections.TryGetValue(hub, out var conn) ? conn.State : HubConnectionState.Disconnected;

        public bool IsConnected(HubName hub) => GetState(hub) == HubConnectionState.Connected;

        /// <summary>Connecte un hub (idempotent).</summary>
        public async Task ConnectAsync(HubName hub, CancellationToken ct = default)
        {
            var gate = _locks.GetOrAdd(hub, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                if (_connections.TryGetValue(hub, out var existing) && existing.State != HubConnectionState.Disconnected)
                {
                    if (existing.State == HubConnectionState.Connected) return;
                    // Attempt to start if not connected
                    await existing.StartAsync(ct).ConfigureAwait(false);
                    return;
                }

                var newConn = BuildConnection(hub);
                WireLifecycleLogs(hub, newConn);
                await newConn.StartAsync(ct).ConfigureAwait(false);
                _connections[hub] = newConn;

                Console.WriteLine($"[SignalR/Multi] CONNECTED -> {hub} ({newConn.ConnectionId ?? "no-connId"})");
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Connects multiple hubs in parallel.</summary>
        public Task ConnectAsync(HubName[] hubs, CancellationToken ct = default)
            => Task.WhenAll(hubs.Select(h => ConnectAsync(h, ct)));

        /// <summary>Ensures the connection of a hub (small backoff).</summary>
        public async Task EnsureConnectedAsync(HubName hub, int maxAttempts = 3, CancellationToken ct = default)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (IsConnected(hub)) return;

                try
                {
                    await ConnectAsync(hub, ct).ConfigureAwait(false);
                    if (IsConnected(hub)) return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SignalR/Multi] EnsureConnected {hub} attempt {attempt} failed: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * attempt, 5)), ct).ConfigureAwait(false);
            }

            throw new InvalidOperationException($"Unable to (re)connect hub {hub}");
        }

        /// <summary>Cleanly disconnects a hub.</summary>
        public async Task DisconnectAsync(HubName hub)
        {
            if (_connections.TryRemove(hub, out var conn))
            {
                try
                {
                    await conn.StopAsync();
                    await conn.DisposeAsync();
                    Console.WriteLine($"[SignalR/Multi] DISCONNECTED -> {hub}");
                }
                catch { /* noop */ }
            }
        }

        /// <summary>Disconnect all hubs.</summary>
        public async Task DisconnectAllAsync()
        {
            var ops = _connections.Keys.Select(DisconnectAsync);
            await Task.WhenAll(ops);
        }

        // ---------------------------
        // Handlers
        // ---------------------------

        public void RegisterHandler<T>(HubName hub, string methodName, Action<T> handler)
        {
            var conn = RequireConnection(hub);
            conn.On(methodName, handler);
            Console.WriteLine($"[SignalR/Multi] Handler registered: {hub}.{methodName} ({typeof(T).Name})");
        }

        public void RegisterHandler(HubName hub, string methodName, Action handler)
        {
            var conn = RequireConnection(hub);
            conn.On(methodName, handler);
            Console.WriteLine($"[SignalR/Multi] Handler registered: {hub}.{methodName} (no payload)");
        }

        // ---------------------------
        // Invoke / Send
        // ---------------------------

        public Task<TResult> InvokeAsync<TResult>(HubName hub, string methodName, CancellationToken ct = default, params object?[] args)
        {
            var conn = RequireConnection(hub);
            return conn.InvokeAsync<TResult>(methodName, args, ct);
        }

        public Task SendAsync(HubName hub, string methodName, CancellationToken ct = default, params object?[] args)
        {
            var conn = RequireConnection(hub);
            return conn.SendAsync(methodName, args, ct);
        }

        // ---------------------------
        // Internals
        // ---------------------------

        private HubConnection RequireConnection(HubName hub)
        {
            if (!_connections.TryGetValue(hub, out var conn))
                throw new InvalidOperationException($"Hub {hub} not connected. Call ConnectAsync({hub}) first.");
            return conn;
        }

        private HubConnection BuildConnection(HubName hub)
        {
            var path = HubRoutes.GetPath(hub);
            var fullUrl = $"{_baseHubUrl}{path}";

            Func<Task<string>>? tokenFactory = null;
            if (_accessTokenProviderAsync != null)
            {
                tokenFactory = async () =>
                {
                    var t = await _accessTokenProviderAsync().ConfigureAwait(false);
                    return t ?? string.Empty;
                };
            }

            var builder = new HubConnectionBuilder()
                .WithUrl(fullUrl, options =>
                {
                    if (tokenFactory != null)
                        options.AccessTokenProvider = tokenFactory;
                })
                .WithAutomaticReconnect();

            return builder.Build();
        }

        private static void WireLifecycleLogs(HubName hub, HubConnection conn)
        {
            conn.Closed += ex =>
            {
                Console.WriteLine($"[SignalR/Multi] CLOSED ({hub}) - {ex?.Message}");
                return Task.CompletedTask;
            };
            conn.Reconnecting += ex =>
            {
                Console.WriteLine($"[SignalR/Multi] RECONNECTING ({hub}) - {ex?.Message}");
                return Task.CompletedTask;
            };
            conn.Reconnected += newId =>
            {
                Console.WriteLine($"[SignalR/Multi] RECONNECTED ({hub}) - ConnectionId={newId}");
                return Task.CompletedTask;
            };
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAllAsync();
            foreach (var gate in _locks.Values) gate.Dispose();
        }
    }
}