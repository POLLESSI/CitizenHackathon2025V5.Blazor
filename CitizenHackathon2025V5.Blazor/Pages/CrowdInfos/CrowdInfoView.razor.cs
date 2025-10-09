using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants;
using CitizenHackathon2025.Shared.StaticConfig.Constants;
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
        private IJSObjectReference? _outZen;
        public List<ClientCrowdInfoDTO> CrowdInfos { get; set; } = new();
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }
        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            var fetched = (await CrowdInfoService.GetAllCrowdInfoAsync()).ToList();
            CrowdInfos = fetched;
            allCrowdInfos = fetched;
            visibleCrowdInfos.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // 2) SignalR (Absolute URL on API side)
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase.TrimEnd('/');

            hubConnection = new HubConnectionBuilder()
                .WithUrl($"{apiBaseUrl}{CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants.CrowdHubMethods.HubPath}", options => 
                {
                    // If your hub is later protected, provide a token
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            // handlers...
            hubConnection.On<ClientCrowdInfoDTO>("ReceiveCrowdUpdate", async dto =>
            {
                void Upsert(List<ClientCrowdInfoDTO> list)
                {
                    var i = list.FindIndex(c => c.Id == dto.Id);
                    if (i >= 0) list[i] = dto; else list.Add(dto);
                }

                Upsert(CrowdInfos);
                Upsert(allCrowdInfos);

                var j = visibleCrowdInfos.FindIndex(c => c.Id == dto.Id);
                if (j >= 0) visibleCrowdInfos[j] = dto;

                await JS.InvokeVoidAsync("window.OutZenInterop.addOrUpdateCrowdMarker",
                    dto.Id.ToString(), dto.Latitude, dto.Longitude, dto.CrowdLevel,
                    new { title = dto.LocationName, description = $"Maj {dto.Timestamp:HH:mm:ss}" });

                await InvokeAsync(StateHasChanged); // <-- important to refresh the UI
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

            // If the base already ends with "/hubs" AND the path begins with "hubs/", we avoid the duplicate
            if (b.EndsWith("/hubs", StringComparison.OrdinalIgnoreCase) &&
                p.StartsWith("hubs/", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring("hubs/".Length); // => "crowdHub"
            }

            return $"{b}/{p}";
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            // 1) Import the module
            _outZen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            // 2) Module boot (creates the map and exposes window.OutZenInterop)
            await _outZen.InvokeVoidAsync("bootOutZen", new
            {
                mapId = "leafletMap",
                center = new double[] { 50.89, 4.34 },
                zoom = 13,
                enableChart = true
            });

            // 3) Initializes the chart via the module export
            await _outZen.InvokeVoidAsync("initCrowdChart", "crowdChart");
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
            try
            {
                if (_outZen is not null)
                {
                    // optional: if you have a destroy function in the module
                    await _outZen.DisposeAsync();
                }
            }
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




