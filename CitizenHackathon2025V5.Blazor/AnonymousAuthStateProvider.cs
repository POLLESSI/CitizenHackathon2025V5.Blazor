using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

public sealed class AnonymousAuthStateProvider : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => Task.FromResult(
            new AuthenticationState(
                new ClaimsPrincipal(new ClaimsIdentity()) // not authenticated
            )
        );
}