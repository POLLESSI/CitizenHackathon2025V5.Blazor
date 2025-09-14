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
                var response = await _httpClient.GetAsync("Event/Latest");
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
                var response = await _httpClient.PostAsJsonAsync("Event/Save", @event);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<EventModel>();
                }
                throw new Exception("Failed to save event");
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
                var response = await _httpClient.GetAsync("Event/Uppcoming-Outdoor");
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
                var response = await _httpClient.PostAsJsonAsync("Event", newEvent);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var createdEvent = await response.Content.ReadFromJsonAsync<EventModel>();
                return createdEvent ?? throw new InvalidOperationException("Response content was null");
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
                var response = await _httpClient.GetAsync($"Event/{id}");
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
                var response = await _httpClient.PostAsync("Event/Archive-Expired", null);
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
    }
}

















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




