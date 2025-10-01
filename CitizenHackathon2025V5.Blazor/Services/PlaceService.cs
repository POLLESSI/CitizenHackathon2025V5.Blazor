using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using SharedDTOs = CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class PlaceService
    {
    #nullable disable
        private readonly HttpClient _httpClient;
        //private const string ApiPlaceBase = "api/Place";
        private string? _placeId;

        public PlaceService(IHttpClientFactory factory)
        {
            _httpClient = factory.CreateClient("ApiWithAuth");
        }
        public async Task<IEnumerable<ClientPlaceDTO?>> GetLatestPlaceAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("Place/Latest");
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<IEnumerable<ClientPlaceDTO>>()
                        ?? Enumerable.Empty<ClientPlaceDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestPlaceAsync: {ex.Message}");
                return Enumerable.Empty<ClientPlaceDTO>();
            }
            
        }
        public async Task<ClientPlaceDTO?> GetPlaceByIdAsync(int id, CancellationToken ct)
        {
            if (id <= 0)
            {
                Console.Error.WriteLine("GetPlaceByIdAsync: invalid id.");
                return null;
            }

            try
            {
                using var resp = await _httpClient.GetAsync($"Place/{id}", ct);

                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return null; 

                resp.EnsureSuccessStatusCode();

                return await resp.Content.ReadFromJsonAsync<ClientPlaceDTO>(cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                // request canceled -> follow the project pattern (no exception re-thrown)
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetPlaceByIdAsync({id}): {ex.Message}");
                return null;
            }
        }
        public async Task<ClientPlaceDTO> SavePlaceAsync(ClientPlaceDTO @place)
        {
            try
            {
                if (@place is null) throw new ArgumentNullException(nameof(@place));
                var response = await _httpClient.PostAsJsonAsync("Place", @place);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<ClientPlaceDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SavePlaceAsync: {ex.Message}");
                return null;
            }
            
        }

        public async Task<int> ArchivePastPlacesAsync()
        {
            try
            {
                var response = await _httpClient.PostAsync("Place/archive-expired", null);
                if (response.StatusCode == HttpStatusCode.NotFound) return 0;
                response.EnsureSuccessStatusCode();

                var archivedCount = await response.Content.ReadFromJsonAsync<int>();
                return archivedCount;
                ;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in ArchivePastPlacesAsync: {ex.Message}");
                throw;
            }

        }
        public ClientPlaceDTO? UpdatePlace(ClientPlaceDTO @place)
            => UpdatePlaceAsync(@place).GetAwaiter().GetResult();
        public async Task<ClientPlaceDTO?> UpdatePlaceAsync(ClientPlaceDTO @place)
        {
            try
            {
                var resp = await _httpClient.PutAsJsonAsync("Place/update", @place);
                if (resp.StatusCode == HttpStatusCode.NotFound) return null;
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<ClientPlaceDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in UpdatePlaceAsync: {ex.Message}");
                return null;
            }

        }
        private sealed class ArchiveResult
        {
            public int ArchivedCount { get; set; }
        }
    }
}





















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




