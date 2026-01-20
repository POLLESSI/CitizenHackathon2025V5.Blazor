using Microsoft.AspNetCore.SignalR.Client;

namespace CitizenHackathon2025V5.Blazor.Client.SignalR
{
    public interface IMultiHubSignalRClient : IAsyncDisposable
    {
        void ConfigureAccessTokenProvider(Func<Task<string?>>? provider);

        Task ConnectAsync(HubName hub, CancellationToken ct = default);
        Task DisconnectAsync(HubName hub);
        Task DisconnectAllAsync();

        HubConnection GetConnection(HubName hub);
        HubConnectionState GetState(HubName hub);
        bool IsConnected(HubName hub);

        Task<HubConnection> GetOrCreateAsync(HubName hub, CancellationToken ct = default);

        Task<TResult> InvokeAsync<TResult>(HubName hub, string methodName, CancellationToken ct = default, params object?[] args);
        Task SendAsync(HubName hub, string methodName, CancellationToken ct = default, params object?[] args);

        IDisposable RegisterHandler<T>(HubName hub, string methodName, Action<T> handler);
        IDisposable RegisterHandler<T>(HubName hub, string methodName, Func<T, Task> handler);
        IDisposable RegisterHandler(HubName hub, string methodName, Action handler);
        IDisposable RegisterHandler(HubName hub, string methodName, Func<Task> handler);
    }
}































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.