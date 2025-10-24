//CrowdInfoCalendarDetail.razor.cs
using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Globalization;
using static CitizenHackathon2025V5.Blazor.Client.Services.CrowdInfoCalendarService;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfoCalendars
{
    public partial class CrowdInfoCalendarDetail : ComponentBase
    {
        [Parameter] public int Id { get; set; }
        private CrowdInfoCalendarModel? model;

        // strings pour saisir HH:mm
        private string? startStr;
        private string? endStr;

        protected override async Task OnInitializedAsync()
        {
            var dto = await Svc.GetByIdAsync(Id);
            model = CrowdInfoCalendarModel.FromDto(dto);
            model.Id = Id;

            startStr = model.StartLocalTime?.ToString(@"hh\:mm");
            endStr = model.EndLocalTime?.ToString(@"hh\:mm");
        }

        private async Task Save()
        {
            // Parse time HH:mm -> TimeSpan?
            model!.StartLocalTime = TryParseTime(startStr);
            model.EndLocalTime = TryParseTime(endStr);

            // ✅ pass the id + the dto
            await Svc.UpdateAsync(Id, model.ToDto());

            Back();
        }

        private static TimeSpan? TryParseTime(string? s)
            => TimeSpan.TryParse(s, out var t) ? t : (TimeSpan?)null;

        private void Back() => Nav.NavigateTo("/crowdcalendar");
        private async Task SoftDelete() { await Svc.SoftDeleteAsync(Id); Back(); }
        private async Task Restore() { await Svc.RestoreAsync(Id); Back(); }
        private async Task HardDelete() { await Svc.HardDeleteAsync(Id); Back(); }
    }
}