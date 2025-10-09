using System.Threading;
using System.Threading.Tasks;
using CitizenHackathon2025V5.Blazor.Client.SignalR;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class CrowdSignalRService : SignalRServiceBase
    {
        public CrowdSignalRService(string baseHubUrl, Func<Task<string?>>? tokenProvider = null)
            : base(baseHubUrl, tokenProvider)
        { }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            await InitializeAsync(HubName.Crowd, ct);

            // Server -> Client Handlers
            RegisterHandler<string>("CrowdRefreshRequested", msg =>
            {
                Console.WriteLine($"[Crowd] CrowdRefreshRequested: {msg}");
                // TODO: relay to UI / state container
            });

            // If necessary: ??ensure that the connection remains up
            await EnsureConnectedAsync(ct: ct);
        }
    }
}

























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.