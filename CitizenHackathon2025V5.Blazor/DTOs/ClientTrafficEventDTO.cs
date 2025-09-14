using CitizenHackathon2025V5.Blazor.Client.Enums;
using System.ComponentModel;
using System.Diagnostics;

namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public class ClientTrafficEventDTO
    {
    #nullable disable

        public string Id { get; set; } = "";
        [DisplayName("Type")]
        public string Type { get; set; } = "";
        [DisplayName("Description")]
        public string Description { get; set; } = "";
        [DisplayName("Latitude")]
        public double Latitude { get; set; }
        [DisplayName("Longitude")]
        public double Longitude { get; set; }
        [DisplayName("Start Time")]
        public DateTime StartTime { get; set; }
        [DisplayName("End Time")]
        public int DelayInSeconds { get; set; } // If applicable
        [DisplayName("Severity Level")]
        public int Level { get; set; } // ex: 1=light, 2=moderate, 3=severe
    }
}

















































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/




