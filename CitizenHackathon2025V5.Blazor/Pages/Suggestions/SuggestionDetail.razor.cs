using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Suggestions
{
    public partial class SuggestionDetail : ComponentBase, IDisposable
    {
#nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public SuggestionService SuggestionService { get; set; } = default!;
        public ClientSuggestionDTO CurrentSuggestion { get; set; }
        [Parameter] public int Id { get; set; }

        private CancellationTokenSource _cts;
        protected override async Task OnParametersSetAsync()
        {
            // Cancels any previous request
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                try
                {
                    CurrentSuggestion = await SuggestionService.GetById(Id, _cts.Token);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[SuggestionDetail] load {Id} failed: {ex.Message}");
                    CurrentSuggestion = null;
                }
            }
            else
            {
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




