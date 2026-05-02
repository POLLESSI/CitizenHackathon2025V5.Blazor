using CitizenHackathon2025.Contracts.DTOs;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Shared
{
    public partial class RainAlertToast
    {
        [Parameter] public RainAlertDTO? Alert { get; set; }
        [Parameter] public EventCallback OnDismiss { get; set; }
        private async Task DismissAsync()
        {
            if (OnDismiss.HasDelegate)
                await OnDismiss.InvokeAsync();
        }
    }
}










































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.