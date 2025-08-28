using System.Net.Http.Json;
using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Utils;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class CrowdInfoService
    {
#nullable disable
        private readonly HttpClient _httpClient;

        public CrowdInfoService(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient("API");
        }

        /// <summary>
        /// Saves or updates a CrowdInfo.
        /// </summary>
        public async Task<CrowdInfoModel?> SaveCrowdInfoAsync(CrowdInfoModel crowdInfo)
        {
            var response = await _httpClient.PostAsJsonAsync("crowdinfo", crowdInfo);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<CrowdInfoModel>();

            return null;
        }

        /// <summary>
        /// Retrieves all CrowdInfo (without pagination).
        /// </summary>
        public async Task<IEnumerable<CrowdInfoModel>> GetAllCrowdInfoAsync()
        {
            var response = await _httpClient.GetAsync("crowdinfo/all");
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<IEnumerable<CrowdInfoModel>>();

            return Enumerable.Empty<CrowdInfoModel>();
        }

        /// <summary>
        /// Retrieves a sorted CrowdInfo page (by date desc) from the API.
        /// </summary>
        public async Task<List<CrowdInfoModel>> GetAllCrowdInfoAsync(int pageIndex, int pageSize)
        {
            var response = await _httpClient.GetAsync($"crowdinfo?pageIndex={pageIndex}&pageSize={pageSize}");

            if (response.IsSuccessStatusCode)
            {
                var list = await response.Content.ReadFromJsonAsync<List<CrowdInfoModel>>();
                return list?.ToNonNullList() ?? new List<CrowdInfoModel>();
            }

            return new List<CrowdInfoModel>();
        }

        /// <summary>
        /// Retrieves all CrowdInfo with non-null fields.
        /// </summary>
        public async Task<List<CrowdInfoModel>> GetLatestCrowdInfoNonNullAsync()
        {
            var raw = await GetAllCrowdInfoAsync();
            return raw.ToNonNullList();
        }

        /// <summary>
        /// Retrieves a CrowdInfo by its ID.
        /// </summary>
        public async Task<CrowdInfoModel?> GetCrowdInfoByIdAsync(int id)
        {
            var response = await _httpClient.GetAsync($"crowdinfo/{id}");
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<CrowdInfoModel>();

            return null;
        }

        /// <summary>
        /// Archive/delete a CrowdInfo by ID.
        /// </summary>
        public async Task<bool> DeleteCrowdInfoAsync(int id)
        {
            var response = await _httpClient.DeleteAsync($"crowdinfo/archive/{id}");
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Method to implement if you want local update before server push.
        /// </summary>
        public CrowdInfoModel UpdateCrowdInfo(CrowdInfoModel crowdInfo)
        {
            // This method is not implemented in the original code snippet.
            // You can implement it as needed, for example:
            throw new NotImplementedException("UpdateCrowdInfo method is not implemented.");
        }
    }
}











































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.