using CitizenHackathon2025V5.Blazor.Client.Models;
using System.Net;
using System.Net.Http.Json;

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
            try
            {
                var response = await _httpClient.GetAsync("Place/Latest");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<PlaceModel>>();
                return list ?? Enumerable.Empty<PlaceModel>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestPlaceAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<PlaceModel> SavePlaceAsync(PlaceModel @place)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("Place", @place);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var saved = await response.Content.ReadFromJsonAsync<PlaceModel>();
                return saved ?? throw new InvalidOperationException("Response content was null");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SavePlaceAsync: {ex.Message}");
                throw;
            }
            
        }
        public PlaceModel? UpdatePlace(PlaceModel @place)
        {
            try
            {
                // This method is not implemented in the original code.
                // You can implement it based on your requirements.
                throw new NotImplementedException("UpdatePlace method is not implemented.");
            }
            catch (Exception)
            {

                throw;
            }
            
        }
    }
}





















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




