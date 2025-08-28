using System.Net.Http.Json;
using CitizenHackathon2025V5.Blazor.Client.Models;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class PlaceService
    {
#nullable disable
        private readonly HttpClient _httpClient;

        public PlaceService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
        public async Task<IEnumerable<PlaceModel?>> GetLatestPlaceAsync()
        {
            var response = await _httpClient.GetAsync("place/latest");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<IEnumerable<PlaceModel?>>();
            }
            return Enumerable.Empty<PlaceModel?>();
        }
        public async Task<PlaceModel> SavePlaceAsync(PlaceModel @place)
        {
            var response = await _httpClient.PostAsJsonAsync("place", @place);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PlaceModel>();
            }
            throw new Exception("Failed to save place");
        }
        public PlaceModel? UpdatePlace(PlaceModel @place)
        {
            // This method is not implemented in the original code.
            // You can implement it based on your requirements.
            throw new NotImplementedException("UpdatePlace method is not implemented.");
        }
    }
}





















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.