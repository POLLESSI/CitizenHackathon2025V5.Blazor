using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using System.Timers;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.TrafficConditions
{
    public partial class TrafficComponent : ComponentBase, IDisposable
    {
        //[Inject] private TrafficServiceBlazor TrafficService { get; set; } = default!;
        //[Inject] private OutZenSignalRService SignalRService { get; set; } = default!;
        //[Inject] private Blazored.Toast.Services.IToastService ToastService { get; set; } = default!;

        private List<ClientTrafficEventDTO>? trafficEvents;
        private System.Timers.Timer? _refreshTimer;

        protected override async Task OnInitializedAsync()
        {
            TrafficService.OnTrafficReceived += events =>
            {
                trafficEvents = events;
                InvokeAsync(StateHasChanged);
            };

            SignalRService.OnCrowdInfoUpdated += OnCrowdInfoReceived;

            await SignalRService.InitializeOutZenAsync(); // <-- méthode correcte de ton service
            await TrafficService.StartAsync();

            StartAutoRefresh();
        }

        private void OnCrowdInfoReceived(ClientCrowdInfoDTO data)
        {
            Console.WriteLine($"?? Received Crowd Info: {data.CrowdLevel}");
        }

        private void StartAutoRefresh()
        {
            _refreshTimer = new System.Timers.Timer(5000); // 5 secondes
            _refreshTimer.Elapsed += async (_, _) =>
            {
                await InvokeAsync(async () => await TrafficService.RequestTraffic());
            };
            _refreshTimer.AutoReset = true;
            _refreshTimer.Start();
        }

        public void Dispose()
        {
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();

            SignalRService.OnCrowdInfoUpdated -= OnCrowdInfoReceived;
        }
    }
}

















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




