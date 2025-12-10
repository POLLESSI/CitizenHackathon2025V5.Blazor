// Client/Services/OutZenJsInterop.cs
using CitizenHackathon2025.Blazor.DTOs;
using Microsoft.JSInterop;

public sealed class OutZenJsInterop : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<OutZenJsInterop>? _selfRef;
    public event Action<ClientCrowdInfoDTO>? OnCrowdInfoUpdated;
    public event Action<List<ClientSuggestionDTO>>? OnSuggestionsUpdated;

    public OutZenJsInterop(IJSRuntime js) => _js = js;

    public async Task StartAsync(string hubUrl, Func<Task<string>> tokenProvider, string? eventId)
    {
        _selfRef = DotNetObjectReference.Create(this);
        // Expose the token function if needed (or pass a callback via advanced JS interop)
        // Here for simplicity: window.getHubToken = async () => await tokenProvider();

        await _js.InvokeVoidAsync("startOutzen", hubUrl, "getHubToken", _selfRef, eventId);
    }

    [JSInvokable] public void OnCrowdInfoUpdatedFromJs(ClientCrowdInfoDTO dto) => OnCrowdInfoUpdated?.Invoke(dto);
    [JSInvokable] public void OnSuggestionsUpdatedFromJs(List<ClientSuggestionDTO> list) => OnSuggestionsUpdated?.Invoke(list);

    public async ValueTask DisposeAsync()
    {
        try { await _js.InvokeVoidAsync("stopOutzen"); } catch { /* ignore */ }
        _selfRef?.Dispose();
    }
}











































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.