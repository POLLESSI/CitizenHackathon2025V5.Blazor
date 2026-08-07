//CrowdInfoCalendarDetail.razor.cs
using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfoCalendars
{
    public partial class CrowdInfoCalendarDetail : ComponentBase, IDisposable
    {
        [Inject] private CrowdInfoCalendarService Svc { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Parameter] public int Id { get; set; }

        /*
         * true when the component is displayed
         * in OutZenDetailModal.
         */
        [Parameter] public bool Embedded { get; set; }
        [Parameter] public EventCallback OnCompleted { get; set; }

        private CrowdInfoCalendarModel? model;

        private TimeOnly? startTime;
        private TimeOnly? endTime;

        private bool _loading;
        private string? _error;
        private int _lastLoadedId;

        private CancellationTokenSource? _cts;

        protected override async Task OnParametersSetAsync()
        {
            if (Id <= 0)
            {
                model = null;
                _error = "Invalid calendar identifier.";
                _loading = false;
                return;
            }

            if (_lastLoadedId == Id && model is not null && string.IsNullOrWhiteSpace(_error))
            {
                return;
            }

            _cts?.Cancel();
            _cts?.Dispose();

            _cts = new CancellationTokenSource();

            var cancellationToken = _cts.Token;

            _loading = true;
            _error = null;
            model = null;

            try
            {
                var dto = await Svc.GetByIdAsync(Id, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (dto is null)
                {
                    _error = $"No calendar entry found for identifier {Id}.";

                    return;
                }

                model = CrowdInfoCalendarModel.FromDto(dto);

                model.Id = dto.Id;

                startTime = model.StartLocalTime is TimeSpan start ? TimeOnly.FromTimeSpan(start) : null;

                endTime = model.EndLocalTime is TimeSpan end ? TimeOnly.FromTimeSpan(end) : null;

                _lastLoadedId = Id;

                Console.WriteLine($"[CrowdCalendarDetail] " + $"Loaded Id={Id}, " + $"ResultId={dto.Id}, " + $"Event={dto.EventName ?? "<null>"}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"[CrowdCalendarDetail] " + $"Loading cancelled for Id={Id}.");
            }
            catch (HttpRequestException ex)
            {
                _error = $"HTTP error while loading " + $"calendar entry #{Id}: {ex.Message}";

                Console.Error.WriteLine($"[CrowdCalendarDetail] {_error}");
            }
            catch (Exception ex)
            {
                _error = $"Unable to load calendar entry " + $"#{Id}: {ex.Message}";

                Console.Error.WriteLine($"[CrowdCalendarDetail] {_error}");
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _loading = false;
                }
            }
        }

        private async Task Save()
        {
            if (model is null)
                return;

            model.StartLocalTime = startTime?.ToTimeSpan();

            model.EndLocalTime = endTime?.ToTimeSpan();

            await Svc.UpdateAsync(Id, model.ToDto());

            await CompleteAsync();
        }

        private async Task CompleteAsync()
        {
            if (Embedded)
            {
                await OnCompleted.InvokeAsync();
                return;
            }

            Nav.NavigateTo("/crowdcalendar");
        }

        private Task Back() => CompleteAsync();

        private async Task SoftDelete()
        {
            await Svc.SoftDeleteAsync(Id);
            await CompleteAsync();
        }

        private async Task Restore()
        {
            await Svc.RestoreAsync(Id);
            await CompleteAsync();
        }

        private async Task HardDelete()
        {
            await Svc.HardDeleteAsync(Id);
            await CompleteAsync();
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


































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/