using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Utils
{
    public static class OutZenDeviceId
    {
        public static async Task<string> GetOrCreateAsync(IJSRuntime js)
        {
            const string key = "outzen.device.id";

            var existing =
                await js.InvokeAsync<string?>(
                    "localStorage.getItem",
                    key);

            if (!string.IsNullOrWhiteSpace(existing))
                return existing;

            var newId = Guid.NewGuid().ToString("N");

            await js.InvokeVoidAsync(
                "localStorage.setItem",
                key,
                newId);

            return newId;
        }
    }
}





































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.