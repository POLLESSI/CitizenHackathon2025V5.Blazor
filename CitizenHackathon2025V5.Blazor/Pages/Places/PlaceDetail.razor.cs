using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Places
{
    public partial class PlaceDetail : ComponentBase, IDisposable
    {
    #nullable disable
        [Inject]
        public HttpClient Client { get; set; }
        [Inject] public PlaceService PlaceService { get; set; } = default!;
        [Inject] public NavigationManager Nav { get; set; } = default!;
        public ClientPlaceDTO? CurrentPlace { get; set; }
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
                var query = QueryHelpers.ParseQuery(uri.Query);
                if (query.TryGetValue("id", out var idValues) &&
                    int.TryParse(idValues.ToString(), out var qid))
                {
                    Id = qid;
                }
            }

            if (Id > 0)
            {
                try
                {
                    Console.WriteLine($"[PlaceDetail] Fetch Id={Id}");
                    CurrentPlace = await PlaceService.GetPlaceByIdAsync(Id, _cts.Token);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[PlaceDetail] load {Id} failed: {ex.Message}");
                    CurrentPlace = null;
                }
            }
            else
            {
                CurrentPlace = null; // Reset if invalid Id
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




