using CitizenHackathon2025V5.Blazor.Client.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.EmergencyIntelligence
{
    public partial class EmergencyIntelligenceTest
    {
        private bool _loading;
        private string? _error;

        private EmergencySourceCheckDTO? _source;
        private EmergencySyncResultDTO? _sync;

        private async Task CheckSourceAsync()
        {
            _loading = true;
            _error = null;
            _sync = null;

            try
            {
                _source = await EmergencyClient.CheckSourceAsync();
                if (_source is null)
                {
                    _error = "The source returned no response.";
                }
            }
            catch (HttpRequestException ex)
            {
                _error =
                    $"Erreur HTTP : {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                _error = "The request to the API was canceled " + "or exceeded the allowed time.";
            }
            catch (Exception ex)
            {
                _error = $"Unexpected error: {ex.Message}";
            }
            finally
            {
                _loading = false;
            }
        }

        private async Task SynchronizeAsync()
        {
            _loading = true;
            _error = null;
            _source = null;

            try
            {
                _sync = await EmergencyClient.SynchronizeAsync();
                if (_sync is null)
                {
                    _error =
                        "The source returned no response.";
                }
            }
            catch (HttpRequestException ex)
            {
                _error =
                    $"Erreur HTTP : {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                _error = "Synchronization was cancelled or timed out.";
            }
            catch (Exception ex)
            {
                _error = $"Unexpected error: {ex.Message}";
            }
            finally
            {
                _loading = false;
            }
        }
    }
}












































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.