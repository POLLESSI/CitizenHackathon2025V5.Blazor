using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.AspNetCore.SignalR.Client;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class EmergencyAlertClientService : IAsyncDisposable
    {
        private HubConnection? _hub;

        private readonly SemaphoreSlim _startGate = new(1, 1);

        public event Func<EmergencyAlertSignalRDTO, Task>? AlertUpserted;
        public event Func<Guid, string, string, Task>? AlertCancelled;
        public event Func<Guid, string, string, Task>? AlertExpired;
        public event Func<EmergencyAlertRefreshDTO, Task>? AlertsRefreshed;
        public event Func<Task>? ConnectionRestored;

        public bool IsConnected => _hub?.State == HubConnectionState.Connected;

        public async Task StartAsync(string hubUrl, Func<Task<string?>> accessTokenProvider, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(hubUrl))
            {
                throw new ArgumentException("Emergency Alert hub URL cannot be empty.", nameof(hubUrl));
            }

            ArgumentNullException.ThrowIfNull(accessTokenProvider);

            await _startGate.WaitAsync(cancellationToken);

            try
            {
                /*
                 * Already connected or currently connecting.
                 */
                if (_hub is not null && _hub.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting)
                {
                    return;
                }

                /*
                 * Clean up a previous disconnected instance.
                 */
                if (_hub is not null)
                {
                    await _hub.DisposeAsync();
                    _hub = null;
                }

                Console.WriteLine($"[EmergencyAlertHub] Connecting to {hubUrl}");

                _hub = new HubConnectionBuilder().WithUrl(hubUrl, options =>
                    {
                        /*
                            * IMPORTANT:
                            *
                            * Do not capture one fixed token.
                            * SignalR will request a fresh token when
                            * needed, including reconnects.
                            */
                        options.AccessTokenProvider = accessTokenProvider;
                    })
                .WithAutomaticReconnect(
                    new[]
                    {
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(20)
                    })
                .Build();

                _hub.ServerTimeout = TimeSpan.FromSeconds(90);

                _hub.KeepAliveInterval = TimeSpan.FromSeconds(15);

                // =============================================
                // UPSERT
                // =============================================

                _hub.On<EmergencyAlertSignalRDTO>(EmergencyAlertHubMethods.ToClient.Upserted,
                    async alert =>
                    {
                        Console.WriteLine(
                            $"[EmergencyAlertHub] UPSERTED " +
                            $"id={alert.Id} " +
                            $"source={alert.SourceCode} " +
                            $"external={alert.ExternalId} " +
                            $"official={alert.IsOfficial}");

                        await InvokeAsync(AlertUpserted, alert);
                    });

                // =============================================
                // CANCEL
                // =============================================

                _hub.On<Guid, string, string>(EmergencyAlertHubMethods.ToClient.Cancelled,
                    async (alertId, sourceCode, externalId) =>
                    {
                        Console.WriteLine($"[EmergencyAlertHub] CANCELLED " + $"id={alertId} " + $"source={sourceCode} " + $"external={externalId}");

                        await InvokeAsync(AlertCancelled, alertId, sourceCode, externalId);
                    });

                // =============================================
                // EXPIRED
                // =============================================

                _hub.On<Guid, string, string>(EmergencyAlertHubMethods.ToClient.Expired,
                    async (alertId, sourceCode, externalId) =>
                    {
                        Console.WriteLine($"[EmergencyAlertHub] EXPIRED " + $"id={alertId} " + $"source={sourceCode} " + $"external={externalId}");

                        await InvokeAsync(AlertExpired, alertId, sourceCode, externalId);
                    });

                // =============================================
                // REFRESH
                // =============================================

                _hub.On<EmergencyAlertRefreshDTO>(EmergencyAlertHubMethods.ToClient.Refreshed,
                    async refresh =>
                    {
                        Console.WriteLine("[EmergencyAlertHub] REFRESHED");

                        await InvokeAsync(AlertsRefreshed, refresh);
                    });

                // =============================================
                // CONNECTION LIFECYCLE
                // =============================================

                _hub.Reconnecting += error =>
                {
                    Console.WriteLine($"[EmergencyAlertHub] RECONNECTING - " +  $"{error?.Message}");

                    return Task.CompletedTask;
                };


                _hub.Reconnected +=
                    async connectionId =>
                    {
                        Console.WriteLine($"[EmergencyAlertHub] RECONNECTED " + $"ConnectionId=" + $"{connectionId ?? "<null>"}");

                        if (_hub is null || _hub.State != HubConnectionState.Connected)
                        {
                            return;
                        }

                        try
                        {
                            /*
                             * SignalR group membership is lost when
                             * a new connection is established.
                             */
                            await _hub.InvokeAsync(EmergencyAlertHubMethods.SubscribeAll);
                            await InvokeAsync(ConnectionRestored);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[EmergencyAlertHub] " + $"Re-subscribe failed: " + $"{ex.Message}");
                        }
                    };


                _hub.Closed += error =>
                {
                    Console.WriteLine($"[EmergencyAlertHub] CLOSED - " + $"{error?.Message}");

                    return Task.CompletedTask;
                };

                try
                {
                    await _hub.StartAsync(cancellationToken);

                    Console.WriteLine($"[EmergencyAlertHub] CONNECTED " + $"ConnectionId=" + $"{_hub.ConnectionId ?? "<null>"}");

                    await _hub.InvokeAsync(EmergencyAlertHubMethods.SubscribeAll, cancellationToken);

                    Console.WriteLine("[EmergencyAlertHub] SubscribeAll OK");
                }
                catch
                {
                    await _hub.DisposeAsync();

                    _hub = null;

                    throw;
                }
            }
            finally
            {
                _startGate.Release();
            }
        }

        private static async Task InvokeAsync(Func<EmergencyAlertSignalRDTO, Task>? handlers, EmergencyAlertSignalRDTO alert)
        {
            if (handlers is null)
                return;

            foreach (var handler in handlers.GetInvocationList().Cast<Func<EmergencyAlertSignalRDTO, Task>>())
            {
                await handler(alert);
            }
        }

        private static async Task InvokeAsync(Func<Guid, string, string, Task>? handlers, Guid alertId, string sourceCode, string externalId)
        {
            if (handlers is null)
                return;

            foreach (var handler in handlers.GetInvocationList().Cast<Func<Guid, string, string, Task>>())
            {
                await handler(alertId, sourceCode, externalId);
            }
        }


        private static async Task InvokeAsync(Func<EmergencyAlertRefreshDTO, Task>? handlers, EmergencyAlertRefreshDTO refresh)
        {
            if (handlers is null)
                return;

            foreach (var handler in handlers.GetInvocationList().Cast<Func<EmergencyAlertRefreshDTO, Task>>())
            {
                await handler(refresh);
            }
        }

        private static async Task InvokeAsync(Func<Task>? handlers)
        {
            if (handlers is null)
                return;

            foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
            {
                await handler();
            }
        }
        public async ValueTask DisposeAsync()
        {
            await _startGate.WaitAsync();

            try
            {
                if (_hub is null)
                    return;

                try
                {
                    if (_hub.State == HubConnectionState.Connected)
                    {
                        await _hub.InvokeAsync(EmergencyAlertHubMethods.UnsubscribeAll);
                    }
                }
                catch
                {
                    /*
                     * Connection may already be gone.
                     */
                }

                await _hub.DisposeAsync();

                _hub = null;
            }
            finally
            {
                _startGate.Release();
            }
        }
    }
}
























































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/