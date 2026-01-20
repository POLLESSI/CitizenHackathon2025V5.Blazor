using System;

namespace CitizenHackathon2025.Blazor.DTOs
{
    public class ClientCrowdInfoCalendarDTO
    {
        public int Id { get; set; }
        public DateTime DateUtc { get; set; }
        public string RegionCode { get; set; } = "";
        public int? PlaceId { get; set; }
        public string? EventName { get; set; }
        public int? ExpectedLevel { get; set; }       // 1..4
        public byte? Confidence { get; set; }         // 0..100
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public TimeSpan? StartLocalTime { get; set; } // HH:mm:ss
        public TimeSpan? EndLocalTime { get; set; }   // HH:mm:ss
        public int LeadHours { get; set; } = 3;
        public string? MessageTemplate { get; set; }
        public string? Tags { get; set; }
        public bool Active { get; set; } = true;
    }
}










































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/