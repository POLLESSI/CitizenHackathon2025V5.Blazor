using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Users
{
    public partial class UserDetail : ComponentBase, IDisposable
    {
    #nullable disable
        [Inject] public HttpClient? Client { get; set; }
        [Inject] public UserService UserService { get; set; } = default!;
        public ClientUserDTO? CurrentUser { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;

        protected override async Task OnParametersSetAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                try
                {
                    CurrentUser = await UserService.GetById(Id);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[UserDetail] load {Id} failed: {ex.Message}");
                    CurrentUser = null;
                }
            }
            else
            {
                CurrentUser = null;
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




