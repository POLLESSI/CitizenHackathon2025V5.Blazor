using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class Privacy
    {
        private string _canvasId = $"rotatingEarth-{Guid.NewGuid():N}";
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;
            await JS.InvokeVoidAsync("initEarth", new
            {
                canvasId = _canvasId,
                speedControlId = _speedId,
                dayUrl = "/images/earth_texture.jpg?v=1",
                nightUrl = "/images/earth_texture_night.jpg?v=1"
            });
        }

        public async ValueTask DisposeAsync()
        {
            try { await JS.InvokeVoidAsync("disposeEarth", _canvasId); } catch { }
        }
    }
}
















































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.