using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using System.Threading;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Places
{
    public partial class PlaceDetail : ComponentBase, IDisposable
    {
#nullable disable
        [Inject]
        public HttpClient Client { get; set; }
        public PlaceModel? CurrentPlace { get; set; }
        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;
        protected override async Task OnParametersSetAsync()
        {
            // Cancels any previous request
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                await GetPlaceAsync(_cts.Token);
            }
            else
            {
                CurrentPlace = null; // Reset if invalid Id
            }
        }
        private async Task GetPlaceAsync(CancellationToken token)
        {
            try
            {
                HttpResponseMessage message = await Client.GetAsync($"api/place/{Id}", token);

                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync(token);
                    CurrentPlace = JsonConvert.DeserializeObject<PlaceModel>(json);
                }
                else
                {
                    CurrentPlace = null;
                }
            }
            catch (TaskCanceledException)
            {
                // Normal cancellation ? we ignore
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading place {Id} : {ex.Message}");
                CurrentPlace = null;
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




