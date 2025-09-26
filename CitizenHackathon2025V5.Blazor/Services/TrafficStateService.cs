using CitizenHackathon2025V5.Blazor.Client.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class TrafficStateService
    {
        public List<ClientTrafficConditionDTO> TrafficConditionList { get; private set; } = new();

        private readonly TrafficConditionService _api;

        public TrafficStateService(TrafficConditionService api)
        {
            _api = api;
        }

        public async Task LoadTrafficAsync()
        {
            var rawTraffic = await _api.GetLatestTrafficConditionAsync();

            TrafficConditionList = rawTraffic
                .Where(tc => tc is not null)
                .Select(tc => tc!)
                .ToList();
        }
    }
}




















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




