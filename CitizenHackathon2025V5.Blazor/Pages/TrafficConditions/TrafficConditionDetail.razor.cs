using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.TrafficConditions
{
    public partial class TrafficConditionDetail : ComponentBase, IDisposable
    {
#nullable disable
        [Inject]
        public HttpClient? Client { get; set; }
        [Inject] public NavigationManager Nav { get; set; } = default!;
        [Inject] public TrafficConditionService TrafficConditionService { get; set; } = default!;
        public ClientTrafficConditionDTO? CurrentTrafficCondition { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;
        protected override async Task OnParametersSetAsync()
        {
            // Cancels any previous request
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id <= 0)
            {
                var uri = new Uri(Nav.Uri);
                var q = QueryHelpers.ParseQuery(uri.Query);
                if (q.TryGetValue("id", out var idValues) && int.TryParse(idValues.ToString(), out var qid))
                    Id = qid;
            }
            if (Id > 0)
            {
                try
                {
                    CurrentTrafficCondition = await TrafficConditionService
                        .GetTrafficConditionByIdAsync(Id, _cts.Token);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[EventDetail] load {Id} failed: {ex.Message}");
                    CurrentTrafficCondition = null;
                }
            }
            else
            {
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




