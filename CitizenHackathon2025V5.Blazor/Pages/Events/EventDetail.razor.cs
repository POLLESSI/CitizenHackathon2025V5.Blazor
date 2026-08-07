using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Events
{
    public partial class EventDetail : ComponentBase, IDisposable
    {
        [Inject]
        private EventService EventService { get; set; } = default!;

        [Parameter]
        public int Id { get; set; }

        private ClientEventDTO? _event;
        private bool _loading;
        private string? _error;
        private int _lastLoadedId;

        private CancellationTokenSource? _cts;

        protected override async Task OnParametersSetAsync()
        {
            if (Id <= 0)
            {
                _event = null;
                _error = "Invalid event identifier.";
                _loading = false;
                return;
            }

            /*
             * Avoid reloading the same event unnecessarily
             * if it has already been successfully loaded.
             */
            if (_lastLoadedId == Id && _event is not null && string.IsNullOrWhiteSpace(_error))
            {
                return;
            }

            /*
             * Cancel any previous request when
             * the user quickly selects another event.
             */
            _cts?.Cancel();
            _cts?.Dispose();

            _cts = new CancellationTokenSource();

            var cancellationToken = _cts.Token;

            _loading = true;
            _error = null;
            _event = null;

            try
            {
                var loadedEvent = await EventService.GetByIdAsync(Id, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                _event = loadedEvent;
                _lastLoadedId = Id;

                if (_event is null)
                {
                    _error = $"No event found for identifier {Id}.";
                    return;
                }

                Console.WriteLine(
                    $"[EventDetail] Loaded Id={Id}, " +
                    $"ResultId={_event.Id}, " +
                    $"Name={_event.Name ?? "<null>"}");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                /*
                 * Normal case when the component is closed
                 * or another event is selected.
                 */
                Console.WriteLine($"[EventDetail] Loading cancelled for Id={Id}.");
            }
            catch (HttpRequestException ex)
            {
                _error = $"HTTP error while loading event #{Id}: {ex.Message}";

                Console.Error.WriteLine($"[EventDetail] {_error}");
            }
            catch (Exception ex)
            {
                _error =
                    $"Unable to load event #{Id}: {ex.Message}";

                Console.Error.WriteLine($"[EventDetail] {_error}");
            }
            finally
            {
                /*
                 * Do not modify the state of a new request
                 * after the previous one has been cancelled.
                 */
                if (!cancellationToken.IsCancellationRequested)
                {
                    _loading = false;
                }
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            GC.SuppressFinalize(this);
        }
    }
}






















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




