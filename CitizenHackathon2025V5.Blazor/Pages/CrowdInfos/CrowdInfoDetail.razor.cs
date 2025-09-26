using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
//using Newtonsoft.Json;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfos
{
    public partial class CrowdInfoDetail : ComponentBase, IDisposable
    {
    #nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public IHttpClientFactory Http { get; set; } = default!;
        [Inject] public CrowdInfoService Crowd { get; set; } = default!;
        public ClientCrowdInfoDTO? CurrentCrowdInfo { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource _cts;
        protected override async Task OnParametersSetAsync()
        {
            //_cts?.Cancel();
            //_cts = new CancellationTokenSource();

            //if (Id <= 0) { CurrentCrowdInfo = null; return; }
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            CurrentCrowdInfo = (Id > 0)
                ? await Crowd.GetCrowdInfoByIdAsync(Id)
                : null;

            try
            {
                var client = Http.CreateClient("ApiWithAuth"); // BaseAddress = https://localhost:7254/api/

                CurrentCrowdInfo = await client.GetFromJsonAsync<ClientCrowdInfoDTO>(
                    $"CrowdInfo/{Id}", _cts.Token);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading Crowd Info {Id}: {ex.Message}");
                CurrentCrowdInfo = null;
            }
        }
        //private async Task GetCrowdInfoAsync(CancellationToken token)
        //{
        //    try
        //    {
        //        HttpResponseMessage message = await Client.GetAsync($"api/event/{Id}", token);

        //        if (message.IsSuccessStatusCode)
        //        {
        //            string json = await message.Content.ReadAsStringAsync(token);
        //            CurrentCrowdInfo = JsonConvert.DeserializeObject<CrowdInfoModel>(json);
        //        }
        //        else
        //        {
        //            CurrentCrowdInfo = null;
        //        }
        //    }
        //    catch (TaskCanceledException)
        //    {
        //        // Normal cancellation ? we ignore
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.Error.WriteLine($"Error loading Crowd Info {Id} : {ex.Message}");
        //        CurrentCrowdInfo = null;
        //    }
        //}

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}





































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




