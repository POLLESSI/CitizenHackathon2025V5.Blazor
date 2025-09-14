using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class JwtAttachHandler : DelegatingHandler
    {
        private readonly IAuthService _auth;

        public JwtAttachHandler(IAuthService auth) => _auth = auth;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // n��crase pas un header d�j� pr�sent
            if (request.Headers.Authorization is null)
            {
                var token = await _auth.GetAccessTokenAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}





