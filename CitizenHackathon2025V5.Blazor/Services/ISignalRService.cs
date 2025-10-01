using CitizenHackathon2025V5.Blazor.Client.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public interface ISignalRService
    {
        event Func<object, Task> OnNotify;
        event Func<CrowdInfoUIDTO, Task> OnCrowdInfoUpdated;
        event Func<ClientEventDTO, Task> OnEventUpdated;
        event Func<ClientTrafficConditionDTO, Task> OnTrafficUpdated;
        event Func<ClientWeatherForecastDTO, Task> OnWeatherForecastUpdated;

        Task StartAsync(string hubUrl, string hubEventName);
        Task StopAsync();
    }
}














































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




