//Pages / CrowdInfoCalendars / CrowdInfoCalendarCreate.razor.cs
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Globalization;
using static CitizenHackathon2025V5.Blazor.Client.Services.CrowdInfoCalendarService;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfoCalendars
{
    public partial class CrowdInfoCalendarCreate : ComponentBase
    {
        [Inject] private CrowdInfoCalendarService Svc { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;

        private CrowdInfoCalendarModel model = new()
        {
            DateUtc = DateTime.UtcNow.Date,
            ExpectedLevel = 2,
            LeadHours = 3,
            Active = true
        };

        private string? startStr;
        private string? endStr;

        private async Task Create()
        {
            model.StartLocalTime = TryParseTime(startStr);
            model.EndLocalTime = TryParseTime(endStr);

            await Svc.CreateAsync(model.ToDto());
            Back();
        }

        private static TimeSpan? TryParseTime(string? s)
            => TimeSpan.TryParse(s, out var t) ? t : (TimeSpan?)null;

        private void Back() => Nav.NavigateTo("/crowdcalendar");
    }
}







































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/