using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using System.Threading;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Events
{
    public partial class EventDetail : ComponentBase, IDisposable
    {
        [Inject] public HttpClient Client { get; set; }

        public EventModel? CurrentEvent { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;

        protected override async Task OnParametersSetAsync()
        {
            // Cancels any previous request
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                await GetEventAsync(_cts.Token);
            }
            else
            {
                CurrentEvent = null; // Reset if invalid Id
            }
        }

        private async Task GetEventAsync(CancellationToken token)
        {
            try
            {
                HttpResponseMessage message = await Client.GetAsync($"api/event/{Id}", token);

                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync(token);
                    CurrentEvent = JsonConvert.DeserializeObject<EventModel>(json);
                }
                else
                {
                    CurrentEvent = null;
                }
            }
            catch (TaskCanceledException)
            {
                // Normal cancellation → we ignore
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading event {Id} : {ex.Message}");
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