using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Events
{
    public partial class EventDetail : ComponentBase, IDisposable
    {
        [Inject] public EventService EventService { get; set; } = default!;

        public ClientEventDTO? CurrentEvent { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;

        protected override async Task OnParametersSetAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                try
                {
                    CurrentEvent = await EventService.GetByIdAsync(Id);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[EventDetail] load {Id} failed: {ex.Message}");
                    CurrentEvent = null;
                }
            }
            else
            {
                CurrentEvent = null;
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




