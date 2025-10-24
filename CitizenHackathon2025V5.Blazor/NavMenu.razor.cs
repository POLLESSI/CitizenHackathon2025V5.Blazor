//NavMenu.razor.cs
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client
{
    public partial class NavMenu : ComponentBase
    {
        [Inject] private NavigationManager NavManager { get; set; } = default!;

        private bool collapseNavMenu = true;

        private void ToggleNavMenu() => collapseNavMenu = !collapseNavMenu;
        private void CloseMenu() => collapseNavMenu = true;

        private bool IsHomePage => NavManager.Uri.EndsWith("/");

        private record MenuItem(string Text, string Href, string Icon);

        private List<MenuItem> MenuItems => new()
        {
            new MenuItem("Accueil", "/", "🏠"),
            new MenuItem("Presentation", "/presentation", "🛡"),
            new MenuItem("Events", "/eventview", "📅"),
            new MenuItem("Crowd Calendar", "/crowdcalendar", "📆"),
            new MenuItem("CrowdCalendar", "/crowdinfocalendar", "📆"),          
            new MenuItem("Create CrowdCalendar", "/crowdinfocalendar/create", "➕"),
            new MenuItem("CrowdInfos (legacy)", "/crowdinfoview", "✨"),
            new MenuItem("GPT Interactions", "/gptinteractionview", "🤖"),
            new MenuItem("Suggestions", "/suggestionview", "💡"),
            new MenuItem("Places", "/placeview", "📍"),
            new MenuItem("Traffic", "/trafficconditionview", "🚦"),
            new MenuItem("Weather", "/weatherforecastview", "🌤"),
            new MenuItem("Users", "/userview", "👤"),
            new MenuItem("Privacy", "/privacy", "🔐"),
            new MenuItem("Help", "/help", "❓"),
            new MenuItem("Map", "/map", "🗺")
        };

        protected override void OnInitialized() =>
            NavManager.LocationChanged += (_, _) => InvokeAsync(StateHasChanged);
    }
}







































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/




