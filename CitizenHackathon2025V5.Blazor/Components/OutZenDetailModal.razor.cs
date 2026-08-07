using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Components
{
    public partial class OutZenDetailModal
    {
        private readonly string _titleId = $"oz-detail-title-{Guid.NewGuid():N}";

        private bool _dragWired;
        private bool _wasOpen;

        [Parameter, EditorRequired]
        public string WindowId { get; set; } = string.Empty;

        [Parameter]
        public bool Open { get; set; }

        [Parameter, EditorRequired]
        public string Title { get; set; } = string.Empty;

        [Parameter]
        public bool CanClose { get; set; } = true;

        [Parameter]
        public bool AllowBackgroundInteraction { get; set; } = true;

        [Parameter]
        public bool CloseOnBackdrop { get; set; }

        [Parameter]
        public EventCallback OnClose { get; set; }

        [Parameter]
        public RenderFragment? ChildContent { get; set; }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            /*
             * The container remains in the DOM.
             * Therefore, we only wire the drag once.
             */
            if (!_dragWired && !string.IsNullOrWhiteSpace(WindowId))
            {
                try
                {
                    _dragWired = await JS.InvokeAsync<bool>("OutZen.safeMakeDrawerDraggable", WindowId);
                }
                catch (JSException ex)
                {
                    Console.Error.WriteLine($"[OutZenDetailModal] " + $"Unable to wire drag for " + $"{WindowId}: {ex.Message}");
                }
            }

            /*
             * On each new opening, the window
             * comes back in front of the other floating windows.
             */
            if (Open && !_wasOpen)
            {
                try
                {
                    await JS.InvokeVoidAsync("OutZen.safeBringToFront", WindowId);
                }
                catch (JSException ex)
                {
                    Console.Error.WriteLine($"[OutZenDetailModal] " + $"Unable to bring {WindowId} " + $"to front: {ex.Message}");
                }
            }

            _wasOpen = Open;
        }

        private string GetOverlayClass()
        {
            var stateClass = Open ? "is-open" : "is-closed";

            var modeClass = AllowBackgroundInteraction ? "is-floating" : "is-modal";

            return $"oz-detail-overlay " + $"{stateClass} " + $"{modeClass}";
        }

        private async Task CloseAsync()
        {
            if (!CanClose)
                return;

            await OnClose.InvokeAsync();
        }

        private async Task CloseFromBackdropAsync()
        {
            if (!Open || AllowBackgroundInteraction || !CloseOnBackdrop)
            {
                return;
            }

            await CloseAsync();
        }
    }
}
































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.