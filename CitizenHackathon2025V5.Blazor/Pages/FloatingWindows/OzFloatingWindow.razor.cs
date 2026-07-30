using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.FloatingWindows
{
    public partial class OzFloatingWindow
    {
        [Parameter, EditorRequired] public string Id { get; set; } = default!;

        [Parameter] public string Title { get; set; } = "Window";
        [Parameter] public string? Subtitle { get; set; }

        [Parameter] public RenderFragment? ChildContent { get; set; }

        [Parameter] public string FabIcon { get; set; } = "Comment";
        [Parameter] public string FabTitle { get; set; } = "Open";
        [Parameter] public string FabRight { get; set; } = "18px";
        [Parameter] public string FabBottom { get; set; } = "18px";
        [Parameter] public int FabZIndex { get; set; } = 9999;

        [Parameter] public string StartLeft { get; set; } = "24px";
        [Parameter] public string StartTop { get; set; } = "120px";
        [Parameter] public int DrawerZIndex { get; set; } = 30000;

        [Parameter] public bool IsOpen { get; set; }
        [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

        [Parameter] public string? Width { get; set; }
        [Parameter] public string? MaxWidth { get; set; }
        [Parameter] public string? MinWidth { get; set; }
        [Parameter] public string? Class { get; set; }

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
                var z = IsPinnedTop ? DrawerZIndex + 30000 : DrawerZIndex;

                var parts = new List<string>
                {
                    "position:fixed",
                    $"left:{StartLeft}",
                    $"top:{StartTop}",
                    "right:auto",
                    "bottom:auto",
                    $"z-index:{z}"
                };

                if (!string.IsNullOrWhiteSpace(Width))
                    parts.Add($"width:{Width}");

                if (!string.IsNullOrWhiteSpace(MaxWidth))
                    parts.Add($"max-width:{MaxWidth}");

                if (!string.IsNullOrWhiteSpace(MinWidth))
                    parts.Add($"min-width:{MinWidth}");

                return string.Join(";", parts) + ";";
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!IsOpen)
            {
                _dragWired = false;
                _resizeWired = false;
                return;
            }

            try
            {
                await JS.InvokeVoidAsync("OutZen.safeBringToFront", Id);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[OzFloatingWindow] " + $"BringToFront failed for {Id}: " + $"{ex.Message}");
            }

            if (!_dragWired)
            {
                try
                {
                    _dragWired = await JS.InvokeAsync<bool>("OutZen.safeMakeDrawerDraggable", Id);
                }
                catch (Exception ex)
                {
                    _dragWired = false;

                    Console.Error.WriteLine($"[OzFloatingWindow] " + $"Draggable wiring failed for {Id}: " + $"{ex.Message}");
                }
            }

            /*
             * A minimized window must no longer be
             * resizable.
             */
            if (IsMinimized)
            {
                _resizeWired = false;
                return;
            }

            if (!_resizeWired)
            {
                try
                {
                    _resizeWired = await JS.InvokeAsync<bool>("OutZen.safeMakeDrawerResizable", Id);
                }
                catch (Exception ex)
                {
                    _resizeWired = false;

                    Console.Error.WriteLine($"[OzFloatingWindow] " + $"Resizable wiring failed for {Id}: " + $"{ex.Message}");
                }
            }
        }

        private async Task ToggleOpen()
        {
            var now = Environment.TickCount64;
            if (now - _lastToggleMs < 180)
                return;

            _lastToggleMs = now;

            IsOpen = !IsOpen;
            _dragWired = false;
            _resizeWired = false;

            Console.WriteLine($"[OzFloatingWindow] ToggleOpen Id={Id}, IsOpen={IsOpen}");

            if (IsOpenChanged.HasDelegate)
                await IsOpenChanged.InvokeAsync(IsOpen);

            StateHasChanged();
        }

        private async Task Close()
        {
            IsOpen = false;
            _dragWired = false;
            _resizeWired = false;
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

        private async Task ToggleMinimize()
        {
            IsMinimized = !IsMinimized;
            _resizeWired = false;

            await InvokeAsync(StateHasChanged);
        }

        private async Task BringToFront()
        {
            try
            {
                await JS.InvokeVoidAsync("OutZen.bringToFront", Id);
            }
            catch { }
        }
    }
}




































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.