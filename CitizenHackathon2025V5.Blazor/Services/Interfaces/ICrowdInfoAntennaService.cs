using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Blazor.DTOs.Security;

namespace CitizenHackathon2025V5.Blazor.Client.Services.Interfaces
{
    public interface ICrowdInfoAntennaService
    {
        Task<List<ClientCrowdInfoAntennaDTO>> GetAllAsync(CancellationToken ct = default);
    }
}
