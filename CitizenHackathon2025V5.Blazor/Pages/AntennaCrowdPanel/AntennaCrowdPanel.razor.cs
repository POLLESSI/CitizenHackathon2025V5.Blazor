using CitizenHackathon2025.Blazor.DTOs;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.AntennaCrowdPanel
{
    public partial class AntennaCrowdPanel
    {
        [Parameter] public int? SelectedEventId { get; set; }

        [Parameter] public int WindowMinutes { get; set; } = 10;
        [Parameter] public double MaxRadiusMeters { get; set; } = 5000;

        [Parameter] public int RefreshSeconds { get; set; } = 10;

        private ClientEventAntennaCrowdDTO? _data;
        private bool _loading;
        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;

        protected override async Task OnParametersSetAsync()
        {
            await ReloadAsync();
            await ResetTimerAsync();
        }

        private async Task ResetTimerAsync()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            _timer?.Dispose();
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, RefreshSeconds)));

            _ = Task.Run(async () =>
            {
                try
                {
                    while (await _timer.WaitForNextTickAsync(_cts.Token))
                    {
                        await InvokeAsync(ReloadAsync);
                    }
                }
                catch { /* ignore */ }
            });
        }

        private async Task ReloadAsync()
        {
            if (SelectedEventId is null)
            {
                _data = null;
                return;
            }

            _loading = true;
            try
            {
                _data = await AntennaCrowdService.GetEventCrowdAsync(SelectedEventId.Value, WindowMinutes, MaxRadiusMeters);
            }
            finally
            {
                _loading = false;
                StateHasChanged();
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _timer?.Dispose();
        }
    }
}
