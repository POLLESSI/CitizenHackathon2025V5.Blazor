using CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services.Interfaces
{
    public interface IGptClientOrchestrator : IAsyncDisposable
    {
        event Func<ClientGptInteractionDTO, Task>? InteractionUpdated;
        event Func<string, Task>? StatusChanged;

        bool EnablePollingFallback { get; set; }
        bool IsHubConnected { get; }

        Task EnsureHubAsync(CancellationToken ct = default);

        Task<GptRunResult> RunAsync(string prompt, double? latitude = null, double? longitude = null, string languageCode = "fr-FR", bool preferAsyncPipeline = true, CancellationToken ct = default);

        Task<bool> CancelCurrentAsync(CancellationToken ct = default);

        Task<bool> CancelAsync(int interactionId, string? requestId = null, CancellationToken ct = default);

        bool TryGetLiveInteraction(int interactionId, out ClientGptInteractionDTO? interaction);

        IReadOnlyCollection<ClientGptInteractionDTO> GetLiveInteractionsSnapshot();
    }

    public sealed class GptRunResult
    {
        public bool Started { get; set; }
        public int? InteractionId { get; set; }
        public string? RequestId { get; set; }
        public string? StatusMessage { get; set; }

        public ClientGptInteractionDTO? PendingInteraction { get; set; }
        public ClientGptInteractionDTO? FinalInteraction { get; set; }
    }
}
































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.