using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class TrafficConditionService
    {
        private readonly HttpClient _httpClient;

        public TrafficConditionService(IHttpClientFactory factory)
        {
            _httpClient = factory.CreateClient("CitizenHackathonAPI");
        }

        /// <summary>
        /// Retrieves the list of latest traffic conditions.
        /// </summary>
        /// <returns>Non-zero list of traffic conditions</returns>
        public async Task<List<TrafficConditionModel>> GetLatestTrafficConditionAsync()
        {
            try
            {
                // HTTP GET call with automatic deserialization
                var response = await _httpClient.GetAsync("trafficcondition/latest");

                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"API call failed with status {response.StatusCode}. Content: {content}");
                }

                var trafficConditions = await response.Content.ReadFromJsonAsync<List<TrafficConditionModel>>();

                return trafficConditions?.ToNonNullList() ?? new List<TrafficConditionModel>();
            }
            catch (HttpRequestException httpEx)
            {
                // Specific HTTP errors
                Console.Error.WriteLine($"HTTP error in GetLatestTrafficConditionAsync: {httpEx.Message}");
                throw;
            }
            catch (NotSupportedException nsEx)
            {
                // Error if content is not JSON
                Console.Error.WriteLine($"The content type is not supported: {nsEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                // Other errors
                Console.Error.WriteLine($"Unexpected error in GetLatestTrafficConditionAsync: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Records a new traffic condition.
        /// </summary>
        /// <param name="trafficCondition">Condition to save</param>
        /// <returns>Condition recorded with server info</returns>
        public async Task<TrafficConditionModel> SaveTrafficConditionAsync(TrafficConditionModel trafficCondition)
        {
            if (trafficCondition == null)
                throw new ArgumentNullException(nameof(trafficCondition));

            try
            {
                var response = await _httpClient.PostAsJsonAsync("trafficcondition", trafficCondition);

                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"API POST failed with status {response.StatusCode}. Content: {content}");
                }

                var savedCondition = await response.Content.ReadFromJsonAsync<TrafficConditionModel>();

                return savedCondition ?? throw new InvalidOperationException("Response content was null");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error in SaveTrafficConditionAsync: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Updates a traffic condition (method to be implemented as needed).
        /// </summary>
        public Task<TrafficConditionModel?> UpdateTrafficConditionAsync(TrafficConditionModel trafficCondition)
        {
            throw new NotImplementedException("UpdateTrafficConditionAsync method is not implemented yet.");
        }
    }
}
















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.