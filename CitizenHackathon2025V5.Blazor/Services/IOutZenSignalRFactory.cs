using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

public interface IOutZenSignalRFactory
{
    Task<string?> GetAccessTokenAsync();
    Task<OutZenSignalRService> CreateAsync(CancellationToken ct = default);
    Task<HubConnection> MessageHubAsync(CancellationToken ct = default);
    Task<HubConnection> SuggestionHubAsync(CancellationToken ct = default);
    Task<HubConnection> AntennaConnectionHubAsync(CancellationToken ct = default);
}

//public sealed class OutZenSignalRFactory : IOutZenSignalRFactory
//{
//    private readonly IMultiHubSignalRClient _multi;
//    public OutZenSignalRFactory(IMultiHubSignalRClient multi) => _multi = multi;

//    public Task<HubConnection> MessageHubAsync(CancellationToken ct = default)
//        => _multi.GetOrCreateAsync(HubName.Message, ct);

//    public Task<HubConnection> SuggestionHubAsync(CancellationToken ct = default)
//        => _multi.GetOrCreateAsync(HubName.Suggestions, ct);

//    public Task<HubConnection> AntennaConnectionHubAsync(CancellationToken ct = default)
//        => _multi.GetOrCreateAsync(HubName.AntennaConnection, ct);

//    public Task<string?> GetAccessTokenAsync()
//    {
//        throw new NotImplementedException();
//    }

//    public Task<OutZenSignalRService> CreateAsync(CancellationToken ct = default)
//    {
//        throw new NotImplementedException();
//    }
//}












































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




