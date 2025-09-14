using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using SharedDTOs = CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class CrowdInfoService
    {
#nullable disable
        private readonly HttpClient _httpClient;
        private readonly IHttpClientFactory _httpFactory;

        public CrowdInfoService(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient("ApiWithAuth");
            _httpFactory = httpClientFactory;
            
        }

        /// <summary>
        /// Saves or updates a CrowdInfo.
        /// </summary>
        public async Task<ClientCrowdInfoDTO?> SaveCrowdInfoAsync(ClientCrowdInfoDTO dto)
        {
            try
            {
                if (dto is null) throw new ArgumentNullException(nameof(dto));
                // we reuse _httpClient already configured
                var resp = await _httpClient.PostAsJsonAsync("crowdinfo", dto);
                resp.EnsureSuccessStatusCode();

                var saved = await resp.Content.ReadFromJsonAsync<ClientCrowdInfoDTO>();
                return saved ?? throw new InvalidOperationException("Response content was null");
            }
            catch (Exception ex)
            {

                Console.Error.WriteLine($"Unexpected error in SaveCrowdInfoAsync: {ex.Message}");
                throw;
            }
            
        }

        /// <summary>
        /// Retrieves all CrowdInfo (without pagination).
        /// </summary>
        public async Task<IEnumerable<ClientCrowdInfoDTO>> GetAllCrowdInfoAsync()
        {
            try
            {

                var response = await _httpClient.GetAsync("CrowdInfo/all");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();

                var list = await response.Content.ReadFromJsonAsync<IEnumerable<ClientCrowdInfoDTO>>();

                return list?.ToNonNullList() ?? new();
            }
            catch (Exception ex)
            {

                Console.Error.WriteLine($"Unexpected error in GetAllCrowdInfoAsync: {ex.Message}");
                throw;
            }
            
        }

        /// <summary>
        /// Retrieves a sorted CrowdInfo page (by date desc) from the API.
        /// </summary>
        public async Task<List<ClientCrowdInfoDTO>> GetAllCrowdInfoAsync(int pageIndex, int pageSize)
        {
            try
            {
                var response = await _httpClient.GetAsync("CrowdInfo/all");

                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                var list = await response.Content.ReadFromJsonAsync<List<ClientCrowdInfoDTO>>();
                
                return new List<ClientCrowdInfoDTO>();
            }
            catch (Exception ex)
            {

                Console.Error.WriteLine($"Unexpected error in GetAllCrowdInfoAsync: {ex.Message}");
                throw;
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
        public async Task<ClientCrowdInfoDTO?> GetCrowdInfoByIdAsync(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"CrowdInfo/{id}");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();

                var item = await response.Content.ReadFromJsonAsync<ClientCrowdInfoDTO>();
                return item;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetCrowdInfoByIdAsync: {ex.Message}");
                throw;
            }
            
        }

        /// <summary>
        /// Archive/delete a CrowdInfo by ID.
        /// </summary>
        public async Task<bool> DeleteCrowdInfoAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"CrowdInfo/archive/{id}");
                response.EnsureSuccessStatusCode();

                var deleted = await response.Content.ReadFromJsonAsync<bool>();
                return deleted;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in DeleteCrowdInfoAsync: {ex.Message}");
                throw;
            }
            
        }

        /// <summary>
        /// Method to implement if you want local update before server push.
        /// </summary>
        public ClientCrowdInfoDTO UpdateCrowdInfo(ClientCrowdInfoDTO crowdInfo)
        {
            // This method is not implemented in the original code snippet.
            // You can implement it as needed, for example:
            throw new NotImplementedException("UpdateCrowdInfo method is not implemented.");
        }
    }
}











































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




