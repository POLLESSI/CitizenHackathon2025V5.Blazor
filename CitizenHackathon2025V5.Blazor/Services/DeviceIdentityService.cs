using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class DeviceIdentityService
    : IDeviceIdentityService
    {
        private readonly IJSRuntime _js;

        public DeviceIdentityService(IJSRuntime js)
        {
            _js = js;
        }

        public async Task<string> GetDeviceIdAsync()
        {
            var id =
                await _js.InvokeAsync<string?>(
                    "OutZenDevice.getOrCreateDeviceId");

            return string.IsNullOrWhiteSpace(id)
                ? Guid.NewGuid().ToString("N")
                : id;
        }
    }
}
