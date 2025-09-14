using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Users
{
    public class UserDetailBase : ComponentBase, IDisposable
    {
        [Inject] public HttpClient? Client { get; set; }
        public UserModel? CurrentUser { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;

        protected override async Task OnParametersSetAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                await GetUserAsync(_cts.Token);
            }
            else
            {
                CurrentUser = null;
            }
        }

        protected async Task GetUserAsync(CancellationToken token)
        {
            try
            {
                HttpResponseMessage message = await Client!.GetAsync($"api/user/{Id}", token);

                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync(token);
                    CurrentUser = JsonConvert.DeserializeObject<UserModel>(json);
                }
                else
                {
                    CurrentUser = null;
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading user {Id} : {ex.Message}");
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




