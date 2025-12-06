using Blazored.Toast.Services;
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Mapster;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using System.Text;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfos
{
    public partial class CrowdInfoCreate
    {
        [Inject] public HttpClient Client { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        private CrowdInfoModel NewCrowdInfo { get; set; } = new CrowdInfoModel();

        protected override Task OnInitializedAsync()
        {
            NewCrowdInfo = new CrowdInfoModel();
            return Task.CompletedTask;
        }
        private async Task Submit()
        {
            try
            {
                var dto = NewCrowdInfo.Adapt<ClientCrowdInfoDTO>();
                var result = await CrowdInfoService.SaveCrowdInfoAsync(dto);

                if (result != null)
                {
                    ToastService.ShowSuccess($"Crowd info '{NewCrowdInfo.LocationName}' successfully registered !");
                    Console.WriteLine($"? CrowdInfo created : {NewCrowdInfo.LocationName} ({NewCrowdInfo.Latitude}, {NewCrowdInfo.Longitude})");

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
                Console.WriteLine($"? Error sending : {ex}");
            }
        }
    }
}


































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




