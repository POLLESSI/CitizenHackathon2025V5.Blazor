using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.FloatingWindows
{
    public partial class OzFloatingWindow
    {
        [Parameter, EditorRequired] public string Id { get; set; } = default!;

        [Parameter] public string Title { get; set; } = "Window";
        [Parameter] public string? Subtitle { get; set; }

        // Content
        [Parameter] public RenderFragment? ChildContent { get; set; }

        // FAB
        [Parameter] public string FabIcon { get; set; } = "Comment";
        [Parameter] public string FabTitle { get; set; } = "Open";
        [Parameter] public string FabRight { get; set; } = "18px";
        [Parameter] public string FabBottom { get; set; } = "18px";
        [Parameter] public int FabZIndex { get; set; } = 9999;

        // Drawer placement defaults
        [Parameter] public string StartLeft { get; set; } = "24px";
        [Parameter] public string StartTop { get; set; } = "120px";
        [Parameter] public int DrawerZIndex { get; set; } = 30000;

        // Controlled open (optional)
        [Parameter] public bool IsOpen { get; set; }
        [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }
        [Parameter] public string? Width { get; set; }          // ex: "360px" / "min(360px, 92vw)"
        [Parameter] public string? MaxWidth { get; set; }       // ex: "360px"
        [Parameter] public string? MinWidth { get; set; }       // ex: "280px"
        [Parameter] public string? Class { get; set; }          // optional for targeting CSS


        // State
        private bool IsDockRight { get; set; }
        private bool IsPinnedTop { get; set; }
        private bool IsMinimized { get; set; }

        private bool _dragWired;
        private bool _resizeWired;
        private long _lastToggleMs;

        private string FabStyle =>
            $"position:fixed;right:{FabRight};bottom:{FabBottom};z-index:{FabZIndex};";

        private string DrawerInlineStyle
        {
            get
            {
                var z = (IsPinnedTop ? DrawerZIndex + 30000 : DrawerZIndex);
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(Width)) parts.Add($"width:{Width}");
                if (!string.IsNullOrWhiteSpace(MaxWidth)) parts.Add($"max-width:{MaxWidth}");
                if (!string.IsNullOrWhiteSpace(MinWidth)) parts.Add($"min-width:{MinWidth}");

                // position
                if (IsDockRight)
                    parts.Add($"z-index:{z}");
                else
                    parts.Add($"left:{StartLeft};top:{StartTop};z-index:{z}");

                return string.Join(";", parts) + ";";
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (IsOpen)
            {
                if (!_dragWired)
                {
                    try { _dragWired = await JS.InvokeAsync<bool>("OutZen.makeDrawerDraggable", Id); }
                    catch { _dragWired = false; }
                }

                if (!_resizeWired)
                {
                    try { _resizeWired = await JS.InvokeAsync<bool>("OutZen.makeDrawerResizable", Id); }
                    catch { _resizeWired = false; }
                }

                await JS.InvokeVoidAsync("OutZen.bringToFront", Id);
                await JS.InvokeVoidAsync("OutZen.avoidOverlap", Id);
            }

            if (!IsOpen)
            {
                _dragWired = false;
                _resizeWired = false;
            }
        }
        private async Task ToggleOpen()
        {
            // anti double-fire (very useful with mixed JS/Blazor inputs)
            var now = Environment.TickCount64;
            if (now - _lastToggleMs < 180) return;
            _lastToggleMs = now;

            IsOpen = !IsOpen;
            _dragWired = false;

            if (IsOpenChanged.HasDelegate)
                await IsOpenChanged.InvokeAsync(IsOpen);

            StateHasChanged();
        }

        private async Task Close()
        {
            IsOpen = false;
            _dragWired = false;
            IsMinimized = false;

            if (IsOpenChanged.HasDelegate)
                await IsOpenChanged.InvokeAsync(IsOpen);

            StateHasChanged();
        }

        private Task TogglePinTop()
        {
            IsPinnedTop = !IsPinnedTop;
            StateHasChanged();
            return Task.CompletedTask;
        }

        private Task ToggleDockRight()
        {
            IsDockRight = !IsDockRight;
            _dragWired = false;
            StateHasChanged();
            return Task.CompletedTask;
        }

        private Task ToggleMinimize()
        {
            IsMinimized = !IsMinimized;
            _dragWired = false;
            StateHasChanged();
            return Task.CompletedTask;
        }

        private async Task BringToFront()
        {
            try { await JS.InvokeVoidAsync("OutZen.bringToFront", Id); }
            catch { /* noop */ }
        }

    }
}




































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.