//CrowdInfoCalendarView.razor.cs

using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using static CitizenHackathon2025V5.Blazor.Client.Services.CrowdInfoCalendarService;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfoCalendars
{
    public partial class CrowdInfoCalendarView : ComponentBase
    {
        private List<ClientCrowdInfoCalendarDTO>? items;

        private DateTime? from;
        private DateTime? to;
        private string? region;
        private int? placeId;
        private string? activeFilter; // "", "true", "false"
        private string _canvasId = $"rotatingEarth-{Guid.NewGuid():N}";
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";

        protected override async Task OnInitializedAsync()
        {
            await LoadAll();
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;
            //await JS.InvokeVoidAsync("initEarth", new
            //{
            //    canvasId = _canvasId,
            //    speedControlId = _speedId,
            //    dayUrl = "/images/earth_texture.jpg?v=1",
            //    nightUrl = "/images/earth_texture_night.jpg?v=1"
            //});
        }

        private void GoNew() => Nav.NavigateTo("/crowdcalendar/new");
        private void GoDetail(int id) => Nav.NavigateTo($"/crowdcalendar/{id}");

        private async Task Load()
        {
            bool? active = activeFilter switch { "true" => true, "false" => false, _ => (bool?)null };
            items = (await Svc.ListAsync(from, to, region, placeId, active)).ToList();
        }

        private async Task LoadAll()
        {
            items = (await Svc.GetAllAsync()).ToList();
        }
        //public async ValueTask DisposeAsync()
        //{
        //    try { await JS.InvokeVoidAsync("disposeEarth", _canvasId); } catch { }
        //}
    }
}