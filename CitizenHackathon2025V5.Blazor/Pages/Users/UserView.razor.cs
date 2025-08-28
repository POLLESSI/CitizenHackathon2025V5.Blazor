using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Pages.Auths;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Users
{
    public partial class UserView : ComponentBase
    {
        [Inject] private AuthService AuthService { get; set; } = default!;
        [Inject] private UserService UserService { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        private UserModel? CurrentUser { get; set; }
        private UserModel? SelectedUser { get; set; }
        private List<UserModel> Users { get; set; } = new();

        private HubConnection? _hubConnection;

        private string? _token;

        protected override async Task OnInitializedAsync()
        {
            var token = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "jwt_token");

            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    var payload = JwtParser.DecodePayload(token);

                    CurrentUser = new UserModel
                    {
                        Email = payload.Email,
                        Role = payload.Role
                    };

                    Users = await GetUsersSecureAsync() ?? new List<UserModel>();
                    await InitSignalR(token);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"UserView initialization error : {ex.Message}");
                }
            }
        }

        private async Task<List<UserModel>?> GetUsersSecureAsync()
        {
            try
            {
                var jwtUsers = await UserService.GetUsersAsync();
                if (jwtUsers == null) return new List<UserModel>();

                return jwtUsers.Select(u => new UserModel
                {
                    Email = u.Email,
                    Role = u.Role
                }).ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"User recovery error : {ex.Message}");
                return new List<UserModel>();
            }
        }

        private void SelectUser(UserModel user)
        {
            SelectedUser = user;
            InvokeAsync(StateHasChanged);
        }

        private async Task Logout()
        {
            await AuthService.LogoutAsync();

            CurrentUser = null;
            SelectedUser = null;
            Users.Clear();
            await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "jwt_token");

            await DisconnectSignalR();
            await InvokeAsync(StateHasChanged);
            await UserService.RemoveAccessTokenAsync();
            _token = null;
        }

        #region SignalR
        private async Task InitSignalR(string token)
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("https://localhost:7254/userhub", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(token);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                .Build();

            _hubConnection.Reconnecting += error =>
            {
                Console.WriteLine($"SignalR: reconnecting due to {error?.Message}");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += connectionId =>
            {
                Console.WriteLine($"SignalR: reconnected with connectionId {connectionId}");
                return Task.CompletedTask;
            };

            _hubConnection.Closed += async error =>
            {
                Console.WriteLine($"SignalR: connection closed. Error: {error?.Message}");
                await Task.Delay(5000);
                try
                {
                    await _hubConnection.StartAsync();
                    Console.WriteLine("SignalR: connection restarted successfully.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SignalR: failed to restart connection: {ex.Message}");
                }
            };

            _hubConnection.On<UserModel>("UserUpdated", (updatedUser) =>
            {
                if (CurrentUser == null) return; // Ignore updates if CurrentUser not defined

                var index = Users.FindIndex(u => u.Email == updatedUser.Email);
                if (index >= 0) Users[index] = updatedUser;
                else Users.Add(updatedUser);

                InvokeAsync(StateHasChanged);
            });

            try
            {
                await _hubConnection.StartAsync();
                Console.WriteLine("SignalR: connection started successfully.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"SignalR: failed to start connection: {ex.Message}");
            }
        }
        private async Task SaveToken()
        {
            await UserService.SetAccessTokenAsync("eyJhbGciOiJIUzI1NiIsInR...");
        }

        private async Task LoadToken()
        {
            _token = await UserService.GetAccessTokenAsync();
        }

        private async Task DisconnectSignalR()
        {
            if (_hubConnection != null)
            {
                try
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SignalR: error during disconnect: {ex.Message}");
                }
                finally
                {
                    _hubConnection = null;
                }
            }
        }
        #endregion
    }
}






























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.