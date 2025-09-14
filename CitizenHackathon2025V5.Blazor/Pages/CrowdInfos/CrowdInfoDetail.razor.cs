using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using System.Threading;

namespace CitizenHackathon2025V4.Blazor.Client.Pages.CrowdInfos
{
    public partial class CrowdInfoDetail : ComponentBase, IDisposable
    {
    #nullable disable
        [Inject] public HttpClient Client { get; set; }
        public CrowdInfoModel CurrentCrowdInfo { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;
        protected override async Task OnParametersSetAsync()
        {
            // Cancels any previous request
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                await GetCrowdInfoAsync(_cts.Token);
            }
            else
            {
                CurrentCrowdInfo = null; // Reset if invalid Id
            }
        }
        private async Task GetCrowdInfoAsync(CancellationToken token)
        {
            try
            {
                HttpResponseMessage message = await Client.GetAsync($"api/event/{Id}", token);

                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync(token);
                    CurrentCrowdInfo = JsonConvert.DeserializeObject<CrowdInfoModel>(json);
                }
                else
                {
                    CurrentCrowdInfo = null;
                }
            }
            catch (TaskCanceledException)
            {
                // Normal cancellation ? we ignore
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading Crowd Info {Id} : {ex.Message}");
                CurrentCrowdInfo = null;
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




