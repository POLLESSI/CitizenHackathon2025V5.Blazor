using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public class OutZenPresentationPageBase : OutZenMapPageBase
    {
        [Inject] protected IJSRuntime JS { get; set; } = default!;
        [Inject] protected NavigationManager Navigation { get; set; } = default!;
        [Inject] protected AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
        protected bool CanModerateComments { get; set; }


        protected override string ScopeKey => "presentation";
        protected override string MapId => "leafletMap";

        protected override bool MapEnabled => true;
        protected override bool EnableHybrid => true;
        protected override bool EnableCluster => true;
        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => true;
        protected override bool ClearAllOnMapReady => true;
        protected override int HybridThreshold => 13;

        protected override (double lat, double lng) DefaultCenter => (50.8503, 4.3517);
        protected override int DefaultZoom => 13;

        protected int CrowdLevel { get; set; } = 55;

        protected string LevelColor => CrowdLevel switch
        {
            < 25 => "#28a745",
            < 50 => "#17a2b8",
            < 75 => "#fd7e14",
            _ => "#dc3545"
        };

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            CanModerateComments =
                user?.Identity?.IsAuthenticated == true &&
                (user.IsInRole("Admin") || user.IsInRole("Moderator"));

            await NotifyDataLoadedAsync(false);
        }

        protected override async Task SeedAsync(bool fit)
        {
            await AddSeedTrafficAsync(fit);
            await AddSeedCrowdAsync(fit);
            await AddSeedWeatherAsync(fit);
        }

        protected async Task ResetPresentationSeeds()
        {
            try
            {
                await JS.InvokeVoidAsync("trafficInterop.clearTrafficMarkers", ScopeKey);
                await JS.InvokeVoidAsync("crowdInterop.clearCrowdMarkers", ScopeKey);
                await JS.InvokeVoidAsync("weatherInterop.clearWeatherMarkers", ScopeKey);

                await AddSeedTrafficAsync(true);
                await AddSeedCrowdAsync(true);
                await AddSeedWeatherAsync(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Presentation] ResetPresentationSeeds failed: {ex.Message}");
            }
        }

        protected async Task AddSeedTrafficAsync(bool fit)
        {
            var items = new[]
            {
                new
                {
                    id = "traffic-seed-1",
                    latitude = 50.8503,
                    longitude = 4.3517,
                    level = 2,
                    title = "Traffic seed",
                    description = "Initial traffic marker for presentation"
                }
            };

            await JS.InvokeVoidAsync("trafficInterop.updateTrafficMarkers", items, ScopeKey, fit, false);
        }

        protected async Task AddSeedCrowdAsync(bool fit)
        {
            var items = new[]
            {
                new
                {
                    id = "crowd-seed-1",
                    latitude = 50.8467,
                    longitude = 4.3525,
                    level = 3,
                    title = "Crowd seed",
                    description = "Initial crowd marker for presentation"
                }
            };

            await JS.InvokeVoidAsync("crowdInterop.updateCrowdMarkers", items, ScopeKey, fit, false);
        }

        protected async Task AddSeedWeatherAsync(bool fit)
        {
            var items = new[]
            {
                new
                {
                    id = "weather-seed-1",
                    latitude = 50.8530,
                    longitude = 4.3498,
                    level = 2,
                    title = "Weather seed",
                    description = "Initial weather marker for presentation",
                    weatherType = "cloudy"
                }
            };

            await JS.InvokeVoidAsync("weatherInterop.updateWeatherMarkers", items, ScopeKey, fit, false);
        }

        protected async Task TestTrafficSignalRReception()
        {
            try
            {
                var items = new[]
                {
                    new
                    {
                        id = $"traffic-test-{Guid.NewGuid():N}",
                        latitude = 50.8950,
                        longitude = 4.3415,
                        level = 2,
                        title = "Manual traffic",
                        description = $"SignalR traffic test - {DateTimeOffset.Now:HH:mm:ss}"
                    }
                };

                await JS.InvokeVoidAsync("trafficInterop.updateTrafficMarkers", items, ScopeKey, true, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Presentation] TestTrafficSignalRReception failed: {ex.Message}");
            }
        }

        protected async Task TestCrowdSignalRReception()
        {
            try
            {
                var items = new[]
                {
                    new
                    {
                        id = $"crowd-test-{Guid.NewGuid():N}",
                        latitude = 50.8612,
                        longitude = 4.3568,
                        level = CrowdLevel switch
                        {
                            < 25 => 1,
                            < 50 => 2,
                            < 75 => 3,
                            _ => 4
                        },
                        title = "Manual crowd",
                        description = $"SignalR crowd test - {CrowdLevel}% - {DateTimeOffset.Now:HH:mm:ss}"
                    }
                };

                await JS.InvokeVoidAsync("crowdInterop.updateCrowdMarkers", items, ScopeKey, true, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Presentation] TestCrowdSignalRReception failed: {ex.Message}");
            }
        }

        protected async Task TestWeatherSignalRReception()
        {
            try
            {
                var items = new[]
                {
                    new
                    {
                        id = $"weather-test-{Guid.NewGuid():N}",
                        latitude = 50.8420,
                        longitude = 4.3600,
                        level = 2,
                        title = "Manual weather",
                        description = $"SignalR weather test - {DateTimeOffset.Now:HH:mm:ss}",
                        weatherType = "rain"
                    }
                };

                await JS.InvokeVoidAsync("weatherInterop.updateWeatherMarkers", items, ScopeKey, true, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Presentation] TestWeatherSignalRReception failed: {ex.Message}");
            }
        }

        protected void NavigateHome()
        {
            Navigation.NavigateTo("/");
        }
    }
}
































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.