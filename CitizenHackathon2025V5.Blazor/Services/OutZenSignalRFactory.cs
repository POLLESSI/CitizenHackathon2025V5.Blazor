using System.Threading.Tasks;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class OutZenSignalRFactory : IOutZenSignalRFactory
    {
        private readonly UserService _userService;
        private readonly EventService _eventService;

        public OutZenSignalRFactory(UserService userService, EventService eventService)
        {
            _userService = userService;
            _eventService = eventService;
        }

        public async Task<OutZenSignalRService> CreateAsync()
        {
            var eventId = _eventService.GetCurrentEvent() ?? "default-event";

            return new OutZenSignalRService(
                baseHubUrl: "https://localhost:7254",
                accessTokenProvider: () => _userService.GetAccessTokenAsync(),
                eventId: eventId
            );
        }
    }
}





































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.