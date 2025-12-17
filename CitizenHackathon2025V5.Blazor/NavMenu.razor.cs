//NavMenu.razor.cs
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client
{
    public partial class NavMenu : ComponentBase, IAsyncDisposable
    {
        private ElementReference _navRef;
        [Inject] private NavigationManager NavManager { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;

        private bool collapseNavMenu = true; // mobile closed by default
        private DotNetObjectReference<NavMenu>? _dotNetRef;

        private async Task ToggleNavMenu()
        {
            collapseNavMenu = !collapseNavMenu;
            await ApplyNavStateAsync();
        }
        private async Task CloseMenu()
        {
            if (!collapseNavMenu) return;
            collapseNavMenu = false;
            await ApplyNavStateAsync();
        }
        private async Task ApplyNavStateAsync()
        {
            // lock scroll
            await JS.InvokeVoidAsync("OutZen.setNavLock", collapseNavMenu);

            // overlay toggle + outside click
            await JS.InvokeVoidAsync("NavMenuInterop.setOpen", _navRef, collapseNavMenu);
        }
        private bool IsHomePage => NavManager.Uri.EndsWith("/");

        [JSInvokable]
        public void CloseFromJs()
        {
            if (!collapseNavMenu)
            {
                collapseNavMenu = true;
                InvokeAsync(StateHasChanged);
            }
        }
        protected override void OnInitialized()
            => NavManager.LocationChanged += async (_, _) => await CloseMenu();
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            _dotNetRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("outzenNav.init", _dotNetRef, "nav.main-nav", ".nav-drawer");
        }
        public ValueTask DisposeAsync()
        {
            _dotNetRef?.Dispose();
            return ValueTask.CompletedTask;
        }
        private record MenuItem(string Text, string Href, string Icon);

        private List<MenuItem> MenuItems => new()
        {
            new MenuItem("Accueil", "/", "🏠"),
            new MenuItem("Presentation", "/presentation", "🛡"),
            new MenuItem("Events", "/eventview", "📅"),
            new MenuItem("Crowd Calendar", "/crowdcalendar", "📆"),       
            //new MenuItem("Create Crowd Calendar", "/crowdinfocalendar/create", "➕"),
            new MenuItem("Crowd Infos", "/crowdinfoview", "✨"),
            new MenuItem("GPT Interactions", "/gptinteractionview", "🤖"),
            new MenuItem("Suggestions", "/suggestionview", "💡"),
            new MenuItem("Places", "/placeview", "📍"),
            new MenuItem("Traffic", "/trafficconditionview", "🚦"),
            new MenuItem("Weather", "/weatherforecastview", "🌤"),
            new MenuItem("Users", "/userview", "👤"),
            new MenuItem("Privacy", "/privacy", "🔐"),
            new MenuItem("Help", "/help", "❓"),
            new MenuItem("Messages", "/messageview", "💬"),
        };

        //protected override void OnInitialized() =>
        //    NavManager.LocationChanged += (_, _) => InvokeAsync(StateHasChanged);
    }
}







































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/




