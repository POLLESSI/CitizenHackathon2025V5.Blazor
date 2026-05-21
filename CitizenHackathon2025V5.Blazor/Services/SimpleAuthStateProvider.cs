using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class SimpleAuthStateProvider : AuthenticationStateProvider
    {
        private static readonly ClaimsPrincipal Anonymous =
            new(new ClaimsIdentity());

        private ClaimsPrincipal _currentUser = Anonymous;

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(new AuthenticationState(_currentUser));
        }

        public void NotifyUserAuthentication(string jwtToken)
        {
            var identity = BuildClaimsIdentity(jwtToken);
            _currentUser = new ClaimsPrincipal(identity);

            NotifyAuthenticationStateChanged(
                Task.FromResult(new AuthenticationState(_currentUser)));
        }

        public void NotifyUserLogout()
        {
            _currentUser = Anonymous;

            NotifyAuthenticationStateChanged(
                Task.FromResult(new AuthenticationState(_currentUser)));
        }

        private static ClaimsIdentity BuildClaimsIdentity(string jwtToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(jwtToken);

            return new ClaimsIdentity(jwt.Claims, authenticationType: "jwt");
        }
    }
}

























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.