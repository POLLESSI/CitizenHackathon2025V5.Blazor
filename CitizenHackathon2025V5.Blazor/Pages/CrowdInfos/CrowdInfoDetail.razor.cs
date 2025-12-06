using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfos
{
    public partial class CrowdInfoDetail : ComponentBase, IDisposable
    {
    #nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public IHttpClientFactory Http { get; set; } = default!;
        [Inject] public CrowdInfoService Crowd { get; set; } = default!;
        public ClientCrowdInfoDTO CurrentCrowdInfo { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource _cts;
        protected override async Task OnParametersSetAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                try
                {
                    CurrentCrowdInfo = await Crowd.GetCrowdInfoByIdAsync(Id, _cts.Token);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error loading Crowd Info {Id}: {ex.Message}");
                    CurrentCrowdInfo = null;
                }
            }
             else
            {
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




