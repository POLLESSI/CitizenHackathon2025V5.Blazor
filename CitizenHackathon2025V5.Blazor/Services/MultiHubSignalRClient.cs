using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class MultiHubSignalRClient : IMultiHubSignalRClient
    {
        private readonly string _baseUrl;
        private readonly ConcurrentDictionary<HubName, HubConnection> _connections = new();
        private readonly SemaphoreSlim _gate = new(1, 1);

        private Func<Task<string?>>? _tokenProvider;
        private bool _disposed;

        private static readonly TimeSpan[] ReconnectDelays =
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20)
        };

        public MultiHubSignalRClient(string baseUrl, Func<Task<string?>>? tokenProvider = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL cannot be null or empty.", nameof(baseUrl));

            _baseUrl = NormalizeBaseUrl(baseUrl);
            _tokenProvider = tokenProvider;
        }

        public void ConfigureAccessTokenProvider(Func<Task<string?>>? provider)
        {
            ThrowIfDisposed();
            _tokenProvider = provider;
        }

        public async Task ConnectAsync(HubName hub, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var connection = await GetOrCreateAsync(hub, ct);

            if (connection.State is HubConnectionState.Connected
                or HubConnectionState.Connecting
                or HubConnectionState.Reconnecting)
            {
                return;
            }

            await _gate.WaitAsync(ct);
            try
            {
                if (connection.State is HubConnectionState.Connected
                    or HubConnectionState.Connecting
                    or HubConnectionState.Reconnecting)
                {
                    return;
                }

                Console.WriteLine($"[SignalR/Multi] CONNECTING -> {hub}");
                await connection.StartAsync(ct);
                Console.WriteLine($"[SignalR/Multi] CONNECTED -> {hub} (ConnId={connection.ConnectionId ?? "<null>"})");
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DisconnectAsync(HubName hub)
        {
            if (_disposed)
                return;

            if (_connections.TryGetValue(hub, out var connection))
            {
                try
                {
                    if (connection.State != HubConnectionState.Disconnected)
                    {
                        await connection.StopAsync();
                    }

                    Console.WriteLine($"[SignalR/Multi] DISCONNECTED -> {hub}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[SignalR/Multi] Disconnect failed for {hub}: {ex}");
                }
            }
        }

        public async Task DisconnectAllAsync()
        {
            if (_disposed)
                return;

            foreach (var hub in _connections.Keys)
            {
                await DisconnectAsync(hub);
            }
        }

        public HubConnection GetConnection(HubName hub)
        {
            ThrowIfDisposed();

            if (_connections.TryGetValue(hub, out var connection))
                return connection;

            throw new InvalidOperationException($"No SignalR connection exists for hub '{hub}'.");
        }

        public HubConnectionState GetState(HubName hub)
        {
            if (_disposed)
                return HubConnectionState.Disconnected;

            if (_connections.TryGetValue(hub, out var connection))
                return connection.State;

            return HubConnectionState.Disconnected;
        }

        public bool IsConnected(HubName hub)
            => GetState(hub) == HubConnectionState.Connected;

        public Task<HubConnection> GetOrCreateAsync(HubName hub, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var connection = _connections.GetOrAdd(hub, BuildConnection);
            return Task.FromResult(connection);
        }

        public Task<TResult> InvokeAsync<TResult>(
            HubName hub,
            string methodName,
            CancellationToken ct = default,
            params object?[] args)
        {
            ThrowIfDisposed();
            return GetConnection(hub).InvokeCoreAsync<TResult>(methodName, args, ct);
        }

        public Task SendAsync(
            HubName hub,
            string methodName,
            CancellationToken ct = default,
            params object?[] args)
        {
            ThrowIfDisposed();
            return GetConnection(hub).SendCoreAsync(methodName, args, ct);
        }

        public IDisposable RegisterHandler<T>(HubName hub, string methodName, Action<T> handler)
        {
            ThrowIfDisposed();

            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            var connection = GetConnection(hub);

            Console.WriteLine($"[SignalR/Multi] Handler registered: {hub}.{methodName} sync ({typeof(T).Name})");
            return connection.On(methodName, handler);
        }

        public IDisposable RegisterHandler<T>(HubName hub, string methodName, Func<T, Task> handler)
        {
            ThrowIfDisposed();

            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            var connection = GetConnection(hub);

            Console.WriteLine($"[SignalR/Multi] Handler registered: {hub}.{methodName} async ({typeof(T).Name})");
            return connection.On(methodName, handler);
        }

        public IDisposable RegisterHandler(HubName hub, string methodName, Action handler)
        {
            ThrowIfDisposed();

            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            var connection = GetConnection(hub);

            Console.WriteLine($"[SignalR/Multi] Handler registered: {hub}.{methodName} sync");
            return connection.On(methodName, handler);
        }

        public IDisposable RegisterHandler(HubName hub, string methodName, Func<Task> handler)
        {
            ThrowIfDisposed();

            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            var connection = GetConnection(hub);

            Console.WriteLine($"[SignalR/Multi] Handler registered: {hub}.{methodName} async");
            return connection.On(methodName, handler);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (var kvp in _connections)
            {
                try
                {
                    await kvp.Value.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[SignalR/Multi] Dispose failed for {kvp.Key}: {ex}");
                }
            }

            _connections.Clear();
            _gate.Dispose();
        }

        private HubConnection BuildConnection(HubName hub)
        {
            var url = BuildHubUrl(_baseUrl, hub);

            Console.WriteLine($"[MultiHubSignalRClient] hub={hub} url={url}");

            var builder = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents;

                    options.AccessTokenProvider = async () =>
                    {
                        try
                        {
                            if (_tokenProvider is null)
                                return string.Empty;

                            var token = await _tokenProvider.Invoke();
                            return token ?? string.Empty;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[SignalR/Multi] AccessTokenProvider failed for {hub}: {ex}");
                            return string.Empty;
                        }
                    };
                })
                .WithAutomaticReconnect(ReconnectDelays);

            var connection = builder.Build();

            // Aligned with the API:
            // API ClientTimeoutInterval = 90s
            // API KeepAliveInterval   = 15s
            //
            // Client side:
            // ServerTimeout must be > KeepAliveInterval server
            // KeepAliveInterval client can remain at 15s
            connection.ServerTimeout = TimeSpan.FromSeconds(90);
            connection.KeepAliveInterval = TimeSpan.FromSeconds(15);

            connection.Reconnecting += error =>
            {
                Console.WriteLine($"[SignalR/Multi] RECONNECTING ({hub}) - {error?.Message}");
                return Task.CompletedTask;
            };

            connection.Reconnected += connectionId =>
            {
                Console.WriteLine($"[SignalR/Multi] RECONNECTED ({hub}) - ConnectionId={connectionId ?? "<null>"}");
                return Task.CompletedTask;
            };

            connection.Closed += error =>
            {
                Console.WriteLine($"[SignalR/Multi] CLOSED ({hub}) - {error?.Message}");
                return Task.CompletedTask;
            };

            return connection;
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            var trimmed = baseUrl.Trim();

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                throw new InvalidOperationException($"Invalid SignalR base URL: '{baseUrl}'.");

            var authority = uri.GetLeftPart(UriPartial.Authority);
            return authority.TrimEnd('/');
        }

        private static string BuildHubUrl(string baseUrl, HubName hub)
        {
            var path = HubRoutes.GetPath(hub);

            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException($"No route is configured for hub '{hub}'.");

            path = path.Trim();

            if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            if (!path.StartsWith("/"))
                path = "/" + path;

            // If on the Contracts side the HubPath is equal to "gptHub",
            // with MapGroup("/hubs"), we want /hubs/gptHub
            if (!path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
                path = "/hubs" + path;

            return $"{baseUrl.TrimEnd('/')}{path}";
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MultiHubSignalRClient));
        }
    }
}









































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.