using System;
using System.ComponentModel.DataAnnotations;
using CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Models
{
    /// <summary>
    /// UI model with DataAnnotations validations.
    /// Used by EditForms on Create/Detail pages.
    /// </summary>
    public class CrowdInfoCalendarModel
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Date (UTC)")]
        public DateTime DateUtc { get; set; }

        [Required, StringLength(32)]
        public string RegionCode { get; set; } = "";

        [Display(Name = "Place Id")]
        public int? PlaceId { get; set; }

        [StringLength(128)]
        public string? EventName { get; set; }

        [Range(1, 4, ErrorMessage = "ExpectedLevel must be between 1 and 4.")]
        public byte ExpectedLevel { get; set; } = 2;

        [Range(0, 100, ErrorMessage = "Confidence must be between 0 and 100.")]
        public byte? Confidence { get; set; }

        [Display(Name = "Start (local)")]
        public TimeSpan? StartLocalTime { get; set; }

        [Display(Name = "End (local)")]
        public TimeSpan? EndLocalTime { get; set; }

        [Range(0, 48, ErrorMessage = "eadHours must be >= 0 and reasonable (<=48).")]
        public int LeadHours { get; set; } = 3;

        [StringLength(512)]
        public string? MessageTemplate { get; set; }

        [StringLength(128)]
        public string? Tags { get; set; }

        public bool Active { get; set; } = true;

        // ---- Mapping helpers (Model <-> DTO) ----
        public static CrowdInfoCalendarModel FromDto(ClientCrowdInfoCalendarDTO d) => new()
        {
            Id = d.Id,
            DateUtc = d.DateUtc,
            RegionCode = d.RegionCode,
            PlaceId = d.PlaceId,
            EventName = d.EventName,
            ExpectedLevel = (byte)(d.ExpectedLevel ?? 0),
            Confidence = d.Confidence,
            StartLocalTime = d.StartLocalTime,
            EndLocalTime = d.EndLocalTime,
            LeadHours = d.LeadHours,
            MessageTemplate = d.MessageTemplate,
            Tags = d.Tags,
            Active = d.Active
        };

        public ClientCrowdInfoCalendarDTO ToDto() => new()
        {
            Id = Id,
            DateUtc = DateUtc,
            RegionCode = RegionCode,
            PlaceId = PlaceId,
            EventName = EventName,
            ExpectedLevel = ExpectedLevel,
            Confidence = Confidence,
            StartLocalTime = StartLocalTime,
            EndLocalTime = EndLocalTime,
            LeadHours = LeadHours,
            MessageTemplate = MessageTemplate,
            Tags = Tags,
            Active = Active
        };
    }
}









































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/