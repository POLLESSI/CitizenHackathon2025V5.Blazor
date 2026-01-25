using CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025.Blazor.DTOs
{
    public sealed class ClientAntennaCountsUpdateDTO
    {
        public int AntennaId { get; set; }
        public ClientAntennaCountsDTO Counts { get; set; } = new();
    }

}
