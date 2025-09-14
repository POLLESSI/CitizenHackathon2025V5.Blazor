using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using System.Threading.Tasks;
using CitizenHackathon2025V5.Blazor.Client.Pages.Auths;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class UserService
    {
    #nullable disable
        private readonly HttpClient _httpClient;
        private readonly IAuthService _authService;
        private readonly IJSRuntime _jsRuntime;
        // Aligns the key with AuthService (JwtTokenKey)
        private const string TokenKey = "access_token";

        public UserService(HttpClient httpClient, IAuthService authService, IJSRuntime jsRuntime)
        {
            _httpClient = httpClient;
            _authService = authService;
            _jsRuntime = jsRuntime;
        }

        public async Task<List<JwtPayload>> GetUsersAsync()
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token)) return new List<JwtPayload>();

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var users = await _httpClient.GetFromJsonAsync<List<JwtPayload>>("user");
                return users ?? [];
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestTrafficConditionAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task SetAccessTokenAsync(string token)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SetAccessTokenAsync: {ex.Message}");
                throw;
            }
            
        }

        public async Task<string?> GetAccessTokenAsync()
        {
            try
            {
                return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", TokenKey);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetAccessTokenAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task RemoveAccessTokenAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in RemoveAccessTokenAsync: {ex.Message}");
                throw;
            }
            
        }
    }
}





















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




