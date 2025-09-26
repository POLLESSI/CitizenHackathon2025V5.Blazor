using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using SharedDTOs = CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class EventService
    {
    #nullable disable
        private readonly HttpClient _httpClient;
        private const string ApiEventBase = "api/Event";
        private string? _eventId;


        public EventService(IHttpClientFactory factory)
        {
            _httpClient = factory.CreateClient("ApiWithAuth");
        }
        public async Task<IEnumerable<ClientEventDTO?>> GetLatestEventAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{ApiEventBase}/latest");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<IEnumerable<ClientEventDTO>>()
                       ?? Enumerable.Empty<ClientEventDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestEventAsync: {ex.Message}");
                return Enumerable.Empty<ClientEventDTO>();
            }
            
        }
        public async Task<ClientEventDTO> SaveEventAsync(ClientEventDTO @event)
        {
            try
            {
                if (@event is null) throw new ArgumentNullException(nameof(@event));
                var resp = await _httpClient.PostAsJsonAsync($"{ApiEventBase}", @event);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<ClientEventDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SaveEventAsync: {ex.Message}");
                return null;
            }
            
        }
        public async Task<IEnumerable<ClientEventDTO>> GetUpcomingOutdoorEventsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{ApiEventBase}/upcoming-outdoor");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<ClientEventDTO>>();
                return list ?? Enumerable.Empty<ClientEventDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetUpcomingOutdoorEventsAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<ClientEventDTO> CreateEventAsync(ClientEventDTO newEvent)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{ApiEventBase}", newEvent);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var createdEvent = await response.Content.ReadFromJsonAsync<ClientEventDTO>();
                return createdEvent ?? throw new InvalidOperationException("Empty response body");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in CreateEventAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<ClientEventDTO?> GetByIdAsync(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{ApiEventBase}/{id}");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ClientEventDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetByIdAsync: {ex.Message}");
                return null;
            }
            
        }
        public async Task<int> ArchivePastEventsAsync()
        {
            try
            {
                var response = await _httpClient.PostAsync($"{ApiEventBase}/archive-expired", null);
                if (response.StatusCode == HttpStatusCode.NotFound) return 0;
                response.EnsureSuccessStatusCode();

                var archivedCount = await response.Content.ReadFromJsonAsync<int>();
                return archivedCount;
                ;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in ArchivePastEventsAsync: {ex.Message}");
                throw;
            }
            
        }
        public void SetCurrentEvent(string eventId) => _eventId = eventId;

        public string? GetCurrentEvent() => _eventId;
        public ClientEventDTO UpdateEvent(ClientEventDTO @event)
            => UpdateEventAsync(@event).GetAwaiter().GetResult();
        public async Task<ClientEventDTO?> UpdateEventAsync(ClientEventDTO @event)
        {
            try
            {
                var resp = await _httpClient.PutAsJsonAsync($"{ApiEventBase}/update", @event);
                if (resp.StatusCode == HttpStatusCode.NotFound) return null;
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<ClientEventDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in UpdateEventAsync: {ex.Message}");
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




