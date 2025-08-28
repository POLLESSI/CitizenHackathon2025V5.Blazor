using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.TrafficConditions
{
    public partial class TrafficConditionDetail : ComponentBase, IDisposable
    {
#nullable disable
        [Inject]
        public HttpClient? Client { get; set; }
        public TrafficConditionModel? CurrentTrafficCondition { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;
        protected override async Task OnParametersSetAsync()
        {
            // Cancels any previous request
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                await GetTrafficConditionAsync(_cts.Token);
            }
            else
            {
                CurrentTrafficCondition = null; // Reset if invalid Id
            }
        }
        private async Task GetTrafficConditionAsync(CancellationToken token)
        {
            try
            {
                HttpResponseMessage message = await Client.GetAsync($"api/event/{Id}", token);

                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync(token);
                    CurrentTrafficCondition = JsonConvert.DeserializeObject<TrafficConditionModel>(json);
                }
                else
                {
                    CurrentTrafficCondition = null;
                }
            }
            catch (TaskCanceledException)
            {
                // Normal cancellation → we ignore
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading traffic condition {Id} : {ex.Message}");
                CurrentTrafficCondition = null;
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