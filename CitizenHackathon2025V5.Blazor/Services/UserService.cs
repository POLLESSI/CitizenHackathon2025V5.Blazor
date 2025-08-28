using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using System.Threading.Tasks;
using CitizenHackathon2025V5.Blazor.Client.Pages.Auths;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class UserService
    {
#nullable disable
        private readonly HttpClient _httpClient;
        private readonly AuthService _authService;
        private string? _accessToken;
        private readonly IJSRuntime _jsRuntime;
        private const string TokenKey = "access_token";

        public UserService(HttpClient httpClient, AuthService authService, IJSRuntime jsRuntime)
        {
            _httpClient = httpClient;
            _authService = authService;
            _jsRuntime = jsRuntime;
        }

        public async Task<List<JwtPayload>> GetUsersAsync()
        {
            var token = await _authService.GetTokenAsync();
            if (string.IsNullOrEmpty(token)) return new List<JwtPayload>();

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var users = await _httpClient.GetFromJsonAsync<List<JwtPayload>>("user");
            return users ?? new List<JwtPayload>();
        }
        public async Task SetAccessTokenAsync(string token)
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
        }

        public async Task<string?> GetAccessTokenAsync()
        {
            return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", TokenKey);
        }
        public async Task RemoveAccessTokenAsync()
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        }
    }
}





















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.