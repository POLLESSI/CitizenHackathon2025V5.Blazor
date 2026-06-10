using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Enums;

namespace CitizenHackathon2025V5.Blazor.Client.Services.Interfaces
{
    public interface IWeatherCriticalAlertClientService
    {
        Task<WeatherAlertResultDTO> SendCriticalWeatherAlertAsync(
            decimal latitude,
            decimal longitude,
            WeatherType weatherType,
            string description);
    }
}

















































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.