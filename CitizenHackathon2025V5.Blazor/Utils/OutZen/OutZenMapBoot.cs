using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Utils.OutZen
{
    public static class OutZenMapBoot
    {
        public static async Task<bool> BootAsync(IJSObjectReference mod, string mapId, object options)
        {
            Console.WriteLine($"[OZ-Boot] BootOutZen requested mapId={mapId}");
            Console.WriteLine($"[OZ-Boot] Stack = {Environment.StackTrace}");
            return await mod.InvokeAsync<bool>("bootOutZen", options);
        }
    }

}







































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.