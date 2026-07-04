using CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services.Interfaces
{
    public interface IGptClientOrchestrator
    {
        event Func<ClientGptInteractionDTO, Task>? InteractionUpdated;

        event Func<ClientGptInteractionDTO, Task>? InteractionCompleted;

        event Func<string?, Task>? StatusChanged;

        Task EnsureHubAsync(
            CancellationToken ct = default);

        Task<ClientGptStartResponseDTO?> StartAsync(string prompt, double? latitude = null, double? longitude = null, string languageCode = "fr-FR", CancellationToken ct = default);

        // RunAsync is kept solely for compatibility.
        Task<bool> CancelCurrentAsync(CancellationToken ct = default);
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