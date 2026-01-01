using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class OutZenSignalRFactory : IOutZenSignalRFactory
    {
        private readonly IConfiguration _config;
        private readonly IAuthService _auth;
        private readonly EventService _eventService;

        public OutZenSignalRFactory(IConfiguration config, IAuthService auth, EventService eventService)
        {
            _config = config;
            _auth = auth;
            _eventService = eventService;
        }

        public async Task<OutZenSignalRService> CreateAsync(CancellationToken ct = default)
        {
            var apiBaseUrl = (_config["ApiBaseUrl"] ?? "https://localhost:7254").TrimEnd('/');
            var hubBaseUrl = (_config["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');

            var eventId = _eventService.GetCurrentEvent() ?? "default-event";

            var svc = new OutZenSignalRService(
                baseHubUrl: hubBaseUrl,
                accessTokenProvider: () => _auth.GetAccessTokenAsync(),
                eventId: eventId);

            // IMPORTANT: your method does not take CT
            await svc.InitializeOutZenAsync();

            return svc;
        }

        // If you want to keep these methods in the interface, they cannot use _multi here.
        // Either you implement them by creating HubConnections on the fly,
        // either you do Option 2 (refactor to _multi).
        public Task<HubConnection> MessageHubAsync(CancellationToken ct = default)
            => throw new NotImplementedException("Use IMultiHubSignalRClient (Option 2) or provide a builder here.");

        public Task<HubConnection> SuggestionHubAsync(CancellationToken ct = default)
            => throw new NotImplementedException("Use IMultiHubSignalRClient (Option 2) or provide a builder here.");

        public Task<HubConnection> AntennaConnectionHubAsync(CancellationToken ct = default)
            => throw new NotImplementedException("Use IMultiHubSignalRClient (Option 2) or provide a builder here.");

        public Task<string?> GetAccessTokenAsync()
            => throw new NotImplementedException("Use IMultiHubSignalRClient (Option 2) or provide a builder here.");
    }
}








































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




