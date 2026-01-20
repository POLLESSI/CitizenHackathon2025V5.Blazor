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














































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




