using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class SimpleAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _anon = new(new ClaimsIdentity());
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(_anon));
    }
}


























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.