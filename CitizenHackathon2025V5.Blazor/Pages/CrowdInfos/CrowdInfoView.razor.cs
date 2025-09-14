using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfos
{
    public partial class CrowdInfoView : IAsyncDisposable
    {
    #nullable disable
        [Inject] public CrowdInfoService CrowdInfoService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }

        private const string ApiBase = "https://localhost:7254";

        // Uses shared DTO to be compatible with SignalR and API
        public List<ClientCrowdInfoDTO> CrowdInfos { get; set; } = new();
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }
        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            CrowdInfos = (await CrowdInfoService.GetAllCrowdInfoAsync()).ToList();

            // 2) SignalR (UNIQUE instance + token)
            var hubUri = new Uri($"{ApiBase}/hubs/crowdHub");
            hubConnection = new HubConnectionBuilder()
                .WithUrl(new Uri("https://localhost:7254/hubs/crowdHub"))
                .WithAutomaticReconnect()
                .Build();

            hubConnection.On<ClientCrowdInfoDTO>("ReceiveCrowdUpdate", async dto =>
            {
                var idx = CrowdInfos.FindIndex(c => c.Id == dto.Id);
                if (idx < 0) CrowdInfos.Add(dto);
                else CrowdInfos[idx] = dto;

                await JS.InvokeVoidAsync("window.OutZenInterop.addOrUpdateCrowdMarker",
                    dto.Id.ToString(), dto.Latitude, dto.Longitude, dto.CrowdLevel,
                    new { title = dto.LocationName, description = $"Maj {dto.Timestamp:HH:mm:ss}" });

                StateHasChanged();
            });

            hubConnection.On<int>("CrowdInfoArchived", async id =>
            {
                CrowdInfos.RemoveAll(c => c.Id == id);
                await JS.InvokeVoidAsync("window.OutZenInterop.removeMarker", id.ToString());
                StateHasChanged();
            });

            try { await hubConnection.StartAsync(); }
            catch (Exception ex) { Console.Error.WriteLine($"[CrowdInfoView] Hub start failed: {ex.Message}"); }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await JS.InvokeVoidAsync("initCrowdChart", "crowdChart");
            }
        }
        private void ClickInfo(int id) => SelectedId = id;

        public async ValueTask DisposeAsync()
        {
            try { await JS.InvokeVoidAsync("OutZenCharts.destroy", "crowdChart"); }
            catch { /* ignore */ }
            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }

        // We go through CrowdInfoService (cleaner) – no more raw Client needed here.
    }
}















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




