using CitizenHackathon2025V5.Blazor.Client.Models;
using System.Net;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class EventService
    {
#nullable disable
        private readonly HttpClient _httpClient;
        private string? _eventId;


        public EventService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
        public async Task<IEnumerable<EventModel?>> GetLatestEventAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/event/latest");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<EventModel?>>();
                return list ?? Enumerable.Empty<EventModel>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestEventAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<EventModel> SaveEventAsync(EventModel @event)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/event/save", @event);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<EventModel>();
                }
                throw new Exception("Empty response body");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SaveEventAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<IEnumerable<EventModel>> GetUpcomingOutdoorEventsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/event/upcoming-outdoor");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<EventModel>>();
                return list ?? Enumerable.Empty<EventModel>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetUpcomingOutdoorEventsAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<EventModel> CreateEventAsync(EventModel newEvent)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/event", newEvent);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var createdEvent = await response.Content.ReadFromJsonAsync<EventModel>();
                return createdEvent ?? throw new InvalidOperationException("Empty response body");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in CreateEventAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<EventModel?> GetByIdAsync(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/event/{id}");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();

                var eventModel = await response.Content.ReadFromJsonAsync<EventModel>();
                return eventModel ?? throw new InvalidOperationException("Id not existing");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetByIdAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<int> ArchivePastEventsAsync()
        {
            try
            {
                var response = await _httpClient.PostAsync("/event/archive-expired", null);
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
        public EventModel UpdateEvent(EventModel @event)
        {
            return @event; // Placeholder for actual update logic
        }
        private sealed class ArchiveResult
        {
            public int ArchivedCount { get; set; }
        }
    }
}

















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




