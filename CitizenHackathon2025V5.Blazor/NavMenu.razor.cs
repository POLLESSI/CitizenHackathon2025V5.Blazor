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
            new MenuItem("Map", "/map", "🗺"),
            new MenuItem("Presentation", "/presentation", "🛡"),
            new MenuItem("Statistics", "/statistics", "📊"),
            new MenuItem("CrowdInfos", "/crowdinfoview", "✨"),
            new MenuItem("Events", "/eventview", "📅"),
            new MenuItem("GPT Interactions", "/gptinteractionview", "🤖"),
            new MenuItem("Places", "/placeview", "📍"),
            new MenuItem("Suggestions", "/suggestionview", "💡"),
            new MenuItem("Traffic", "/trafficconditionview", "🚦"),
            new MenuItem("Users", "/userview", "👤"),
            new MenuItem("Weather", "/weatherforecastview", "🌤"),
            new MenuItem("Privacy", "/privacy", "🔐"),
            new MenuItem("Help", "/help", "❓")
        };

        protected override void OnInitialized() =>
            NavManager.LocationChanged += (_, _) => InvokeAsync(StateHasChanged);
    }
}







































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/