using System.ComponentModel.DataAnnotations;

namespace CitizenHackathon2025V5.Blazor.Client.Models
{
    public class CrowdInfoModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Location name is required.")]
        [StringLength(100, ErrorMessage = "The name cannot exceed 100 characters.")]
        public string LocationName { get; set; }

        [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
        public double Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
        public double Longitude { get; set; }

        [Range(0, 10, ErrorMessage = "The crowd level must be between 0 and 10.")]
        public int CrowdLevel { get; set; }

        [Required(ErrorMessage = "Date/time is required.")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}























































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/




