using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfos
{
    public partial class CrowdInfoView : IAsyncDisposable
    {
    #nullable disable
        [Inject] public CrowdInfoService CrowdInfoService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }


        private const string ApiBase = "https://localhost:7254";
        public List<ClientCrowdInfoDTO> CrowdInfos { get; set; } = new();
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }
        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            CrowdInfos = (await CrowdInfoService.GetAllCrowdInfoAsync()).ToList();
            LoadMoreItems();

            // 2) SignalR (Absolute URL on API side)
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase.TrimEnd('/');

            var hubPath = "/hubs/crowdHub";

            var hubUrl = BuildHubUrl(apiBaseUrl, hubPath);

            hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        // Get your JWT here (via IAuthService, etc.)
                        var token = await Auth.GetAccessTokenAsync();
                        return token ?? string.Empty;
                    };
                })
                .WithAutomaticReconnect()
                .Build();

            // handlers...
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

            //await GetCrowdInfo();
        }
        private static string BuildHubUrl(string baseUrl, string path)
        {
            var b = baseUrl.TrimEnd('/');
            var p = path.TrimStart('/'); // ex: "hubs/crowdHub"

            // Si la base se termine déjà par "/hubs" ET le path commence par "hubs/", on évite le doublon
            if (b.EndsWith("/hubs", StringComparison.OrdinalIgnoreCase) &&
                p.StartsWith("hubs/", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring("hubs/".Length); // => "crowdHub"
            }

            return $"{b}/{p}";
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await JS.InvokeVoidAsync("initCrowdChart", "crowdChart");
            }
        }
        private void ClickInfo(int id) => SelectedId = id;

        //private async Task GetCrowdInfo()
        //{
        //    var list = await CrowdInfoService.GetAllCrowdInfoAsync();
        //    CrowdInfos = list?.ToList() ?? new();
        //    //var http = HttpFactory.CreateClient("ApiWithAuth"); // BaseAddress = https://localhost:7254/api/
        //    //using var resp = await http.GetAsync("CrowdInfo/All"); // → /api/CrowdInfo/All
        //    //resp.EnsureSuccessStatusCode();

        //    //var json = await resp.Content.ReadAsStringAsync();
        //    //CrowdInfos = System.Text.Json.JsonSerializer
        //    //    .Deserialize<List<ClientCrowdInfoDTO>>(json) ?? new();
        //}

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
    }
}















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




