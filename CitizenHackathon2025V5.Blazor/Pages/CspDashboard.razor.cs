using CitizenHackathon2025.DTOs.Security;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class CspDashboard
    {
        private List<CspReportContent>? reports;

        protected override async Task OnInitializedAsync()
        {
            reports = await Http.GetFromJsonAsync<List<CspReportContent>>("csp-report/all") ?? new();
        }

        private async Task ClearReports()
        {
            await Http.DeleteAsync("csp-report/clear");
            reports = new();
        }
    }
}







































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.