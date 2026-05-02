using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class CrowdSafetyDashboard
    {
        private string? _loadError;
        private HubConnection? _hub;
        private readonly List<CrowdSafetyAlertDTO> _alerts = new();

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var existing = await Http.GetFromJsonAsync<List<CrowdSafetyAlertDTO>>(
                    "crowd/safety-alerts/latest?limit=50");

                if (existing is not null)
                    _alerts.AddRange(existing);
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
            }

            try
            {
                _hub = new HubConnectionBuilder()
                    .WithUrl("https://localhost:7254/hubs/crowdSafetyHub", options =>
                    {
                        options.AccessTokenProvider = async () =>
                            await Auth.GetAccessTokenAsync() ?? string.Empty;
                    })
                    .WithAutomaticReconnect()
                    .Build();

                _hub.On<CrowdSafetyAlertDTO>(
                    CrowdSafetyHubMethods.ToClient.CrowdSafetyAlertRaised,
                    alert =>
                    {
                        if (_alerts.All(a => a.Id != alert.Id))
                            _alerts.Insert(0, alert);

                        InvokeAsync(StateHasChanged);
                    });

                await _hub.StartAsync();
            }
            catch (Exception ex)
            {
                _loadError = $"Connexion SignalR impossible : {ex.Message}";
            }
        }

        private static string GetCardClass(CrowdSafetyAlertDTO alert)
            => alert.Severity switch
            {
                >= 4 => "alert-card critical blink",
                3 => "alert-card high blink-soft",
                2 => "alert-card medium",
                _ => "alert-card low"
            };

        private static string GetIcon(byte severity)
            => severity switch
            {
                >= 4 => "🔥",
                3 => "🚨",
                2 => "⚠️",
                _ => "ℹ️"
            };

        public async ValueTask DisposeAsync()
        {
            if (_hub is not null)
            {
                try
                {
                    await _hub.StopAsync();
                }
                catch { }

                await _hub.DisposeAsync();
            }
        }
    }
}































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
