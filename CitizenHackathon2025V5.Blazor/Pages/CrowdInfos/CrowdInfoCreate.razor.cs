using Blazored.Toast.Services;
using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using System.Text;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfos
{
    public partial class CrowdInfoCreate
    {
        [Inject]
        public HttpClient Client { get; set; }  // Injection HttpClient
        [Inject]
        public NavigationManager Navigation { get; set; }
        private CrowdInfoModel NewCrowdInfo { get; set; } = new CrowdInfoModel();

        protected override async Task OnInitializedAsync()
        {
            NewCrowdInfo = new CrowdInfoModel();
        }
        private async Task Submit()
        {
            try
            {
                var result = await CrowdInfoService.SaveCrowdInfoAsync(NewCrowdInfo);

                if (result != null)
                {
                    ToastService.ShowSuccess($"Crowd info '{NewCrowdInfo.LocationName}' successfully registered !");
                    Console.WriteLine($"✅ CrowdInfo created : {NewCrowdInfo.LocationName} ({NewCrowdInfo.Latitude}, {NewCrowdInfo.Longitude})");

                    // Reset the form in dev mode
                    NewCrowdInfo = new CrowdInfoModel();
                }
                else
                {
                    ToastService.ShowError("Registration failed. Check API connection.");
                }
            }
            catch (Exception ex)
            {
                ToastService.ShowError($"Error : {ex.Message}");
                Console.WriteLine($"❌ Error sending : {ex}");
            }
        }
    }
}


































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.