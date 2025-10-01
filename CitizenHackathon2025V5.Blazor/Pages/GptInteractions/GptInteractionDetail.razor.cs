using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions
{
    public partial class GptInteractionDetail : ComponentBase, IDisposable
    {
#nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public GptInteractionService GptInteractionService { get; set; } = default!;
        public ClientGptInteractionDTO? CurrentGptInteraction { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;
        protected override async Task OnParametersSetAsync()
        {
            // Cancels any previous request
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                try
                {
                    CurrentGptInteraction = await GptInteractionService.GetByIdAsync(Id);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[EventDetail] load {Id} failed: {ex.Message}");
                    CurrentGptInteraction = null;
                }
            }
            else
            {
                CurrentGptInteraction = null; // Reset if invalid Id
            }
        }
        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}







































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




