using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;

public sealed class MultiHubSignalRClient : IMultiHubSignalRClient
{
    private readonly string _baseUrl;                       // ex: https://localhost:7254
    private Func<Task<string?>> _tokenProvider;             // JWT (mutable via ConfigureAccessTokenProvider)
    private readonly ConcurrentDictionary<HubName, HubConnection> _connections = new();
    private readonly ConcurrentDictionary<HubName, SemaphoreSlim> _locks = new();

    public MultiHubSignalRClient(string baseUrl, Func<Task<string?>> tokenProvider)
    {
        _baseUrl = (baseUrl ?? throw new ArgumentNullException(nameof(baseUrl))).TrimEnd('/');
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public void ConfigureAccessTokenProvider(Func<Task<string?>>? provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        _tokenProvider = provider;
    }

    public HubConnection GetConnection(HubName hub) => RequireConnection(hub);

    public HubConnectionState GetState(HubName hub)
        => _connections.TryGetValue(hub, out var conn) ? conn.State : HubConnectionState.Disconnected;

    public bool IsConnected(HubName hub) => GetState(hub) == HubConnectionState.Connected;

    public async Task<HubConnection> GetOrCreateAsync(HubName hub, CancellationToken ct = default)
    {
        await ConnectAsync(hub, ct);
        return RequireConnection(hub);
    }

    public async Task ConnectAsync(HubName hub, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(hub, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_connections.TryGetValue(hub, out var existing))
            {
                if (existing.State == HubConnectionState.Connected)
                    return;

                if (existing.State == HubConnectionState.Disconnected)
                {
                    await existing.StartAsync(ct).ConfigureAwait(false);
                }
                return;
            }

            var conn = BuildConnection(hub);
            WireLifecycleLogs(hub, conn);

            await conn.StartAsync(ct).ConfigureAwait(false);
            _connections[hub] = conn;

            Console.WriteLine($"[SignalR/Multi] CONNECTED -> {hub} (ConnId={conn.ConnectionId ?? "n/a"})");
        }
        finally
        {
            gate.Release();
        }
    }

    public Task ConnectAsync(HubName[] hubs, CancellationToken ct = default)
        => Task.WhenAll(hubs.Select(h => ConnectAsync(h, ct)));

    public async Task EnsureConnectedAsync(HubName hub, int maxAttempts = 3, CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
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

    public async Task DisconnectAsync(HubName hub)
    {
        if (_connections.TryRemove(hub, out var conn))
        {
            try { await conn.StopAsync().ConfigureAwait(false); } catch { }
            try { await conn.DisposeAsync().ConfigureAwait(false); } catch { }
            Console.WriteLine($"[SignalR/Multi] DISCONNECTED -> {hub}");
        }
    }

    public async Task DisconnectAllAsync()
    {
        var hubs = _connections.Keys.ToArray();
        foreach (var hub in hubs)
            await DisconnectAsync(hub).ConfigureAwait(false);
    }

    public Task<TResult> InvokeAsync<TResult>(HubName hub, string methodName, CancellationToken ct = default, params object?[] args)
    {
        var conn = RequireConnection(hub);
        var payload = (args is { Length: > 0 }) ? args : Array.Empty<object?>();
        return conn.InvokeCoreAsync<TResult>(methodName, payload, ct);
    }

    public Task SendAsync(HubName hub, string methodName, CancellationToken ct = default, params object?[] args)
    {
        var conn = RequireConnection(hub);
        var payload = (args is { Length: > 0 }) ? args : Array.Empty<object?>();
        return conn.SendAsync(methodName, payload, ct);
    }

    public IDisposable RegisterHandler<T>(HubName hub, string methodName, Action<T> handler)
    {
        var conn = RequireConnection(hub);
        var sub = conn.On(methodName, handler);
        Console.WriteLine($"[SignalR/Multi] Handler registered: {hub}.{methodName} ({typeof(T).Name})");
        return sub;
    }

    public IDisposable RegisterHandler<T>(HubName hub, string methodName, Func<T, Task> handler)
    {
        var conn = RequireConnection(hub);
        var sub = conn.On(methodName, handler);
        Console.WriteLine($"[SignalR/Multi] Handler registered: {hub}.{methodName} async ({typeof(T).Name})");
        return sub;
    }

    public IDisposable RegisterHandler(HubName hub, string methodName, Action handler)
    {
        var conn = RequireConnection(hub);
        var sub = conn.On(methodName, handler);
        Console.WriteLine($"[SignalR/Multi] Handler registered: {hub}.{methodName} (no payload)");
        return sub;
    }

    public IDisposable RegisterHandler(HubName hub, string methodName, Func<Task> handler)
    {
        var conn = RequireConnection(hub);
        var sub = conn.On(methodName, handler);
        Console.WriteLine($"[SignalR/Multi] Handler registered: {hub}.{methodName} async (no payload)");
        return sub;
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
        var url = HubRoutes.BuildUrl(_baseUrl, hub);

        Func<Task<string?>> tokenFactory = async () => await _tokenProvider().ConfigureAwait(false);

        var conn = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.AccessTokenProvider = tokenFactory;
            })
            .WithAutomaticReconnect()
            .Build();

        return conn;
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
        await DisconnectAllAsync().ConfigureAwait(false);
        foreach (var gate in _locks.Values) gate.Dispose();
    }
}




































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.