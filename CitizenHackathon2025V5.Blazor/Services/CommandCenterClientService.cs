using CitizenHackathon2025.Contracts.DTOs;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class CommandCenterClientService
    {
        private readonly HttpClient _http;

        public CommandCenterClientService(IHttpClientFactory factory)
        {
            _http = factory.CreateClient("ApiWithAuth");
        }

        public async Task<CommandCenterSnapshotDTO?> GetSnapshotAsync()
        {
            return await _http.GetFromJsonAsync<CommandCenterSnapshotDTO>("commandcenter/snapshot");
        }

        public async Task<List<CrowdAlertCluster>> GetIncidentsAsync()
        {
            return await _http.GetFromJsonAsync<List<CrowdAlertCluster>>("commandcenter/incidents") ?? new();
        }

        public async Task<List<DecisionActionDTO>> GetDecisionActionsAsync()
        {
            return await _http.GetFromJsonAsync<List<DecisionActionDTO>>(
                "commandcenter/actions") ?? new();
        }
    }
}
