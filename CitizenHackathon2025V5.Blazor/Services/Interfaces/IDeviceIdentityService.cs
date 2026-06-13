namespace CitizenHackathon2025V5.Blazor.Client.Services.Interfaces
{
    public interface IDeviceIdentityService
    {
        Task<string> GetDeviceIdAsync();
    }
}
