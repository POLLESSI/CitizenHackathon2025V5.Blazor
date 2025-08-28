using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using System.Threading;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Suggestions
{
    public partial class SuggestionDetail : ComponentBase, IDisposable
    {
#nullable disable
        [Inject] public HttpClient? Client { get; set; }
        public SuggestionModel? CurrentSuggestion { get; set; }
        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;
        protected override async Task OnParametersSetAsync()
        {
            // Cancels any previous request
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                await GetSuggestionAsync(_cts.Token);
            }
            else
            {
                CurrentSuggestion = null; // Reset if invalid Id
            }
        }
        private async Task GetSuggestionAsync(CancellationToken token)
        {
            try
            {
                HttpResponseMessage message = await Client.GetAsync($"api/suggestion/{Id}", token);

                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync(token);
                    CurrentSuggestion = JsonConvert.DeserializeObject<SuggestionModel>(json);
                }
                else
                {
                    CurrentSuggestion = null;
                }
            }
            catch (TaskCanceledException)
            {
                // Normal cancellation → we ignore
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading suggestion {Id} : {ex.Message}");
                CurrentSuggestion = null;
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