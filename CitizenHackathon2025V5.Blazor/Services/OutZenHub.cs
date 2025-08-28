using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Shared.CrowdInfo;
using CitizenHackathon2025V5.Blazor.Client.Shared.GptInteraction;
using CitizenHackathon2025V5.Blazor.Client.Shared.Suggestion;
using CitizenHackathon2025V5.Blazor.Client.Shared.TrafficCondition;
using CitizenHackathon2025V5.Blazor.Client.Shared.WeatherForecast;
using Microsoft.AspNetCore.SignalR;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class OutzenHub : Hub
    {
        public async Task SendCrowdInfo(CrowdInfoDTO dto)
            => await Clients.All.SendAsync("ReceiveCrowdInfo", dto);

        public async Task SendTrafficInfo(List<TrafficConditionDTO> data)
            => await Clients.All.SendAsync("ReceiveTrafficInfo", data);

        public async Task SendSuggestions(List<SuggestionDTO> suggestions)
            => await Clients.All.SendAsync("ReceiveSuggestions", suggestions);

        public async Task SendWeatherForecast(List<WeatherForecastDTO> forecasts)
            => await Clients.All.SendAsync("ReceiveWeatherForecast", forecasts);

        public async Task SendGptInteraction(GptInteractionDTO interaction)
        {
            await Clients.All.SendAsync("ReceiveGptInteraction", interaction);
        }
    }
}




























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.