using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Pages.Auths;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Users
{
    public partial class UserView : ComponentBase
    {
    #nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] private AuthService AuthService { get; set; } = default!;
        [Inject] private UserService UserService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private const string ApiBase = "https://localhost:7254";
        private IJSObjectReference _outZen;

        private ClientUserDTO CurrentUser { get; set; }
        private ClientUserDTO SelectedUser { get; set; }
        private List<ClientUserDTO> Users { get; set; } = new();
        private List<ClientUserDTO> allUsers = new();
        private List<ClientUserDTO> visibleUsers = new();
        private int currentIndex = 0;
        private const int PageSize = 20;
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";

        private HubConnection hubConnection;

        private string _token;

        protected override async Task OnInitializedAsync()
        {
            await LoadToken(); // charge _token depuis UserService

            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";
            var hubBaseUrl = Config["SignalR:HubBase"]?.TrimEnd('/')
                             ?? $"{apiBaseUrl}/hubs";

            var url = $"{hubBaseUrl}{HubPaths.User}"; // => https://localhost:7254/hubs/userHub

            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            // 🔽 et ici on utilise _token, pas token
            if (!string.IsNullOrEmpty(_token))
            {
                try
                {
                    var payload = JwtParser.DecodePayload(_token);

                    CurrentUser = new ClientUserDTO
                    {
                        Email = payload.Email,
                        Role = payload.Role
                    };

                    await InitSignalR(_token);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"UserView initialization error : {ex.Message}");
                }
            }
        }

        private async Task<List<ClientUserDTO>> GetUsersSecureAsync()
        {
            try
            {
                var jwtUsers = await UserService.GetUsersAsync();
                if (jwtUsers == null) return new List<ClientUserDTO>();

                return jwtUsers.Select(u => new ClientUserDTO
                {
                    Email = u.Email,
                    Role = u.Role
                }).ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"User recovery error : {ex.Message}");
                return new List<ClientUserDTO>();
            }
        }

        private void SelectUser(ClientUserDTO user)
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
            hubConnection = new HubConnectionBuilder()
                .WithUrl("https://localhost:7254/hubs/userHub", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(token);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                .Build();
            await hubConnection.StartAsync();

            hubConnection.Reconnecting += error =>
            {
                Console.WriteLine($"SignalR: reconnecting due to {error?.Message}");
                return Task.CompletedTask;
            };

            hubConnection.Reconnected += connectionId =>
            {
                Console.WriteLine($"SignalR: reconnected with connectionId {connectionId}");
                return Task.CompletedTask;
            };

            hubConnection.Closed += async error =>
            {
                Console.WriteLine($"SignalR: connection closed. Error: {error?.Message}");
                await Task.Delay(5000);
                try
                {
                    await hubConnection.StartAsync();
                    Console.WriteLine("SignalR: connection restarted successfully.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SignalR: failed to restart connection: {ex.Message}");
                }
            };

            hubConnection.On<ClientUserDTO>("UserUpdated", (updatedUser) =>
            {
                if (CurrentUser == null) return; // Ignore updates if CurrentUser not defined

                var index = Users.FindIndex(u => u.Email == updatedUser.Email);
                if (index >= 0) Users[index] = updatedUser;
                else Users.Add(updatedUser);

                InvokeAsync(StateHasChanged);
            });

            try
            {
                await hubConnection.StartAsync();
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
            if (hubConnection != null)
            {
                try
                {
                    await hubConnection.StopAsync();
                    await hubConnection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SignalR: error during disconnect: {ex.Message}");
                }
                finally
                {
                    hubConnection = null;
                }
            }
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_outZen is not null)
                {
                    await _outZen.DisposeAsync();
                }
            }
            catch { }

            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }
        #endregion
    }
}






























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




