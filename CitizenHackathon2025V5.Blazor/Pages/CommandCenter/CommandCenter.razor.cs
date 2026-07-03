using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CommandCenter
{
    public partial class CommandCenter : OutZenMapPageBase
    {
        [Inject] public CommandCenterClientService CommandCenterService { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;

        protected override string ScopeKey => "commandcenter";
        protected override string MapId => "leafletMap-commandcenter";

        protected override bool EnableCluster => false;
        protected override bool EnableHybrid => true;
        protected override bool EnableWeatherLegend => true;

        public CommandCenterSnapshotDTO? Snapshot { get; set; }

        public List<CrowdAlertCluster> Clusters { get; set; } = new();

        protected override async Task OnInitializedAsync()
        {
            Snapshot = await CommandCenterService.GetSnapshotAsync();
            Clusters = await CommandCenterService.GetIncidentsAsync();
        }

        protected override async Task SeedAsync(bool fit)
        {
            // Temporary: as long as the JS function addOrUpdateCommandCenterClusters does not exist.
            await Task.CompletedTask;
        }
    }
}


























































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.