using CitizenHackathon2025V5.Blazor.Client.Pages.Auths;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class AuthService : IAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly IJSRuntime _js;
        private readonly AuthenticationStateProvider _provider;

        private const string JwtTokenKey = "jwt_token";
        private const string RefreshTokenKey = "refresh_token";

        public AuthService(HttpClient httpClient, IJSRuntime js, AuthenticationStateProvider provider)
        {
            _httpClient = httpClient;
            _js = js;
            _provider = provider;
        }

        // ?? Login with JWT recovery + RefreshToken
        public async Task<bool> LoginAsync(string email, string password)
        {
            var payload = new { email, password };
            var response = await _httpClient.PostAsJsonAsync("auth/login", payload);

            if (!response.IsSuccessStatusCode) return false;

            // Here we assume that the API returns { accessToken, refreshToken }
            var json = await response.Content.ReadAsStringAsync();
            var loginResult = JsonSerializer.Deserialize<LoginResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (loginResult is null) return false;

            // Client-side storage
            await _js.InvokeVoidAsync("localStorage.setItem", JwtTokenKey, loginResult.AccessToken);
            await _js.InvokeVoidAsync("localStorage.setItem", RefreshTokenKey, loginResult.RefreshToken);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

            return true;
        }

        // ?? Manual OR auto logout (via beforeunload in JS)
        public async Task LogoutAsync()
        {
            var refreshToken = await _js.InvokeAsync<string>("localStorage.getItem", RefreshTokenKey);

            if (!string.IsNullOrEmpty(refreshToken))
            {
                // Interop call to notify the API
                await _js.InvokeVoidAsync("outzenLogout.autoLogout", refreshToken);
            }

            // Client-side cleanup
            await _js.InvokeVoidAsync("localStorage.removeItem", JwtTokenKey);
            await _js.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);

            _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        // ?? Retrieve the JWT token in memory
        public async Task<string?> GetAccessTokenAsync()
        {
            // version pragmatique : lis le localStorage où tu stockes le token à la connexion
            var token = await _js.InvokeAsync<string>("localStorage.getItem", JwtTokenKey);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }

        // ?? Retrieve the refresh token (useful for auto refresh)
        public async Task<string?> GetRefreshTokenAsync()
        {
            var token = await _js.InvokeAsync<string>("localStorage.getItem", RefreshTokenKey);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }

        // DTO to deserialize the login response
        private class LoginResponse
        {
            public string AccessToken { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
        }

    }
}








































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




