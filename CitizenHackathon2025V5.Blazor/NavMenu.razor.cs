//NavMenu.razor.cs
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client
{
    public partial class NavMenu : ComponentBase, IAsyncDisposable
    {
        private ElementReference _navRef;
        private DotNetObjectReference<NavMenu>? _dotNetRef;
        [Inject] private NavigationManager NavManager { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;

        /*private bool collapseNavMenu = true;*/ // mobile closed by default
        private bool isMenuOpen;

        private async Task ToggleNavMenu()
        {
            isMenuOpen = !isMenuOpen;
            await ApplyNavStateAsync();
        }

        private async Task CloseMenu()
        {
            if (!isMenuOpen) return;
            isMenuOpen = false;
            await ApplyNavStateAsync();
        }

        private async Task ApplyNavStateAsync()
        {
            await JS.InvokeVoidAsync("OutZen.setNavLock", isMenuOpen);
            await JS.InvokeVoidAsync("OutZen.nav.setOpen", _navRef, isMenuOpen);
        }

        [JSInvokable] public Task CloseFromJs() => CloseMenu();

        protected override void OnInitialized()
        {
            NavManager.LocationChanged += OnLocationChanged;
        }
        private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
        {
            _ = InvokeAsync(CloseMenu);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;
            _dotNetRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("OutZen.nav.init", _dotNetRef, "nav.main-nav", ".nav-drawer");
        }

        public ValueTask DisposeAsync()
        {
            NavManager.LocationChanged -= OnLocationChanged;

            _dotNetRef?.Dispose();
            _dotNetRef = null;

            return ValueTask.CompletedTask;
        }

        private sealed record MenuItem(string Text, string Href, string Icon, bool IconOnly = false);
        private sealed record MenuGroup(string Key, string Title, string CssClass, IReadOnlyList<MenuItem> Items);

        private static readonly IReadOnlyList<MenuGroup> MenuGroups =
        new List<MenuGroup>
        {
            new(
                Key: "outzen",
                Title: "OUTZEN",
                CssClass: "oz-nav-group--outzen",
                Items: new List<MenuItem>
                {
                    new("Home", "/", "🏠", IconOnly: true),
                    new("OutZen Interactions", "/gptinteractionview", "🤖"),
                    new("Crowd Infos", "/crowdinfoview", "✨"),
                    new("Historic Suggestions", "/suggestionview", "💡"),
                }),

            new(
                Key: "data",
                Title: "DONNÉES",
                CssClass: "oz-nav-group--data",
                Items: new List<MenuItem>
                {
                    new("Antenna Crowd Panel", "/antennacrowdpanel", "📡"),
                    new("Crowd Calendar", "/crowdcalendar", "📆"),
                    new("Events", "/eventview", "📅"),
                    new("Places", "/placeview", "📍"),
                    new("Traffic", "/trafficconditionview", "🚦"),
                    new("Weather", "/weatherforecastview", "🌤️")
                }),

            new(
                Key: "community",
                Title: "COMMUNAUTÉ",
                CssClass: "oz-nav-group--community",
                Items: new List<MenuItem>
                {
                    new("Comments", "/messageview", "💬"),
                    new("GDPR", "/privacy", "🔐"),
                    new("Help", "/help", "❓")
                }),

            new(
                Key: "operations",
                Title: "OPÉRATIONS",
                CssClass: "oz-nav-group--operations",
                Items: new List<MenuItem>
                {
                    new("Command Center", "/commandcenter", "🛰️"),
                    new("Presentation", "/presentation", "🛡️")
                }),

            new(
                Key: "ecosystem",
                Title: "ÉCOSYSTEME / RESSOURCES",
                CssClass: "oz-nav-group--ecosystem",
                Items: new List<MenuItem>
                {
                    new("WalOnMap", "/rssgeoportail", "🗺️"),
                    new("Géoportail Monitoring", "/geoportail-feed", "📡"),
                    new("WallonieEnPoche", "/wallonieenpoche", "📱")
                })
        };
    }
}







































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/




