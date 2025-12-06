using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
//using CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class CrowdInfoService
    {
    #nullable disable
        private readonly HttpClient _httpClient;
        //private const string ApiCrowdBase = "api/CrowdInfo";

        public CrowdInfoService(IHttpClientFactory factory)
        {
            _httpClient = factory.CreateClient("ApiWithAuth");
        }

        /// <summary>
        /// Retrieves all CrowdInfo (without pagination).
        /// </summary>
        public async Task<IEnumerable<ClientCrowdInfoDTO>> GetAllCrowdInfoAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("CrowdInfo/all");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<IEnumerable<ClientCrowdInfoDTO>>()
                       ?? Enumerable.Empty<ClientCrowdInfoDTO>();
            }
            catch (Exception ex)
            {

                Console.Error.WriteLine($"Unexpected error in GetAllCrowdInfoAsync: {ex.Message}");
                return Enumerable.Empty<ClientCrowdInfoDTO>();
            }

        }

        /// <summary>
        /// Retrieves all CrowdInfo with non-null fields.
        /// </summary>
        public async Task<List<ClientCrowdInfoDTO>> GetLatestCrowdInfoNonNullAsync()
        {
            try
            {
                var raw = await GetAllCrowdInfoAsync();
                return raw.ToNonNullList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetAllCrowdInfoNonNullAsync: {ex.Message}");
                throw;
            }

        }

        /// <summary>
        /// Retrieves a CrowdInfo by its ID.
        /// </summary>
        public async Task<ClientCrowdInfoDTO> GetCrowdInfoByIdAsync(int id, CancellationToken ct = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync($"CrowdInfo/{id}", ct);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ClientCrowdInfoDTO>(cancellationToken: ct);
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetCrowdInfoByIdAsync: {ex.Message}");
                return null;
            }
        }
        public async Task<IEnumerable<ClientCrowdInfoDTO>> GetLatestAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("CrowdInfo/latest");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<IEnumerable<ClientCrowdInfoDTO>>()
                       ?? Enumerable.Empty<ClientCrowdInfoDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestAsync: {ex.Message}");
                throw;
            }

        }

        public async Task<IEnumerable<ClientCrowdInfoDTO>> GetByLocationAsync(string locationName)
        {
            try
            {
                var resp = await _httpClient.GetAsync($"CrowdInfo/by-location?locationName={Uri.EscapeDataString(locationName)}");
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<IEnumerable<ClientCrowdInfoDTO>>()
                       ?? Enumerable.Empty<ClientCrowdInfoDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetByLocationAsync: {ex.Message}");
                return Enumerable.Empty<ClientCrowdInfoDTO>();
            }
            
        }

        /// <summary>
        /// Saves or updates a CrowdInfo.
        /// </summary>
        public async Task<ClientCrowdInfoDTO> SaveCrowdInfoAsync(ClientCrowdInfoDTO dto)
        {
            try
            {
                if (dto is null) throw new ArgumentNullException(nameof(dto));
                var resp = await _httpClient.PostAsJsonAsync("CrowdInfo", dto);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<ClientCrowdInfoDTO>();
            }
            catch (Exception ex)
            {

                Console.Error.WriteLine($"Unexpected error in SaveCrowdInfoAsync: {ex.Message}");
                return null;
            }
            
        }

        /// <summary>
        /// Archive/delete a CrowdInfo by ID.
        /// </summary>
        public async Task<bool> DeleteCrowdInfoAsync(int id)
        {
            try
            {
                var resp = await _httpClient.DeleteAsync($"CrowdInfo/archive/{id}");
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in DeleteCrowdInfoAsync: {ex.Message}");
                return false;
            }
            
        }

        /// <summary>
        /// Method to implement if you want local update before server push.
        /// </summary>
        public ClientCrowdInfoDTO UpdateCrowdInfo(ClientCrowdInfoDTO dto)
            => UpdateCrowdInfoAsync(dto).GetAwaiter().GetResult();

        public async Task<ClientCrowdInfoDTO> UpdateCrowdInfoAsync(ClientCrowdInfoDTO dto)
        {
            try
            {
                var resp = await _httpClient.PutAsJsonAsync("CrowdInfo/update", dto);
                if (resp.StatusCode == HttpStatusCode.NotFound) return null;
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<ClientCrowdInfoDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in UpdateCrowdInfoAsync: {ex.Message}");
                return null;
            }
            
        }
    }
}











































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




