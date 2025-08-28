using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Auths
{
    public static class JSInterop
    {
        public static IJSRuntime? _js;

        public static void Init(IJSRuntime js) => _js = js;

        public static async Task SetLocalStorage(string key, string value) =>
            await _js!.InvokeVoidAsync("jsInterop.setLocalStorage", key, value);

        public static async Task<string?> GetLocalStorage(string key) =>
            await _js!.InvokeAsync<string>("jsInterop.getLocalStorage", key);

        public static async Task RemoveLocalStorage(string key) =>
            await _js!.InvokeVoidAsync("jsInterop.removeLocalStorage", key);
    }
}
