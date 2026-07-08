using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CommandCenter
{
    public partial class CommandCenter : OutZenMapPageBase
    {
        [Inject] public CommandCenterClientService CommandCenterService { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;

        protected override string ScopeKey => "commandcenter";
        protected override string MapId => "leafletMap-commandcenter";

        protected override bool EnableCluster => false;
        protected override bool EnableHybrid => false;
        protected override bool EnableWeatherLegend => false;

        protected override (double lat, double lng) DefaultCenter => (50.45, 4.75);
        protected override int DefaultZoom => 8;

        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => true;
        protected override bool ClearAllOnMapReady => false;

        private CancellationTokenSource? _refreshCts;
        private Task? _refreshTask;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
        //private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

        private const bool VerboseCommandCenterLogs = false;
        private int TotalClusterCount => Clusters.Count;
        private int TotalFusedAlertCount =>
            Clusters.Sum(c => c.AlertCount);
        private int TotalActiveConnectionCount =>
            Clusters.Sum(c => c.TotalActiveConnections);
        private int TotalUniqueDeviceCount =>
            Clusters.Sum(c => c.TotalUniqueDevices);

        private int? _lastClusterCount;
        private int? _lastFusedAlertCount;
        private int? _lastActiveConnectionCount;
        private int? _lastUniqueDeviceCount;

        public CommandCenterSnapshotDTO? Snapshot { get; set; }
        public List<CrowdAlertCluster> Clusters { get; set; } = new();
        public List<DecisionActionDTO> Actions { get; set; } = new();

        protected override async Task OnInitializedAsync()
        {
            await LoadCommandCenterDataAsync();

            foreach (var cluster in Clusters)
            {
                Console.WriteLine(
                    $"[COMMAND CENTER] Cluster {cluster.ZoneName} lat={cluster.Latitude} lng={cluster.Longitude} risk={cluster.RiskScore}");
            }

            await NotifyDataLoadedAsync(fit: true);

            StartAutoRefresh();
        }

        private async Task LoadCommandCenterDataAsync(CancellationToken ct = default)
        {
            // Les méthodes client n’acceptent pas encore forcément ct.
            // Ce n’est pas bloquant, mais on garde ct pour l’évolution future.
            Snapshot = await CommandCenterService.GetSnapshotAsync();
            Clusters = await CommandCenterService.GetIncidentsAsync();
            Actions = await CommandCenterService.GetDecisionActionsAsync();

            LogInfo($"Data loaded. risk={Snapshot?.GlobalRiskScore}, clusters={Clusters.Count}");

            LogCounterChanges();
        }

        protected override async Task SeedAsync(bool fit)
        {
            Console.WriteLine($"[COMMAND CENTER] SeedAsync called. fit={fit}, clusters={Clusters.Count}, booted={IsMapBooted}");

            if (Clusters.Count == 0)
            {
                await PruneAllCommandCenterAlertMarkersAsync();
                return;
            }

            var activeMarkerKeys = new List<string>();

            foreach (var cluster in Clusters)
            {
                if (!IsValidCoordinate(cluster.Latitude, cluster.Longitude))
                {
                    Console.WriteLine($"[COMMAND CENTER] Invalid cluster coords skipped: {cluster.ZoneName} {cluster.Latitude},{cluster.Longitude}");

                    continue;
                }

                var markerKey = BuildMarkerKey(cluster);
                activeMarkerKeys.Add(markerKey);

                var payload = new
                {
                    AntennaId = markerKey,

                    Latitude = cluster.Latitude,
                    Longitude = cluster.Longitude,

                    ActiveConnections = cluster.TotalActiveConnections,
                    UniqueDevices = cluster.TotalUniqueDevices,
                    Severity = cluster.Severity,

                    Title = cluster.ZoneName,
                    Message =
                        $"Risk score: {cluster.RiskScore}/100<br>" +
                        $"Alerts fused: {cluster.AlertCount}<br>" +
                        $"Estimated population: {cluster.EstimatedPopulation}<br>" +
                        $"Antennas: {BuildAntennaList(cluster)}"
                };

                try
                {
                    var ok = await JS.InvokeAsync<bool>(
                        "OutZenInterop.__esm.addOrUpdateAntennaAlertCircle",
                        payload,
                        ScopeKey);

                    LogVerbose($"Marker added={ok} key={markerKey} zone={cluster.ZoneName}");
                }
                catch (JSException ex)
                {
                    Console.WriteLine($"[COMMAND CENTER] addOrUpdateAntennaAlertCircle failed for {markerKey}: {ex.Message}");
                }
            }

            await PruneCommandCenterAlertMarkersAsync(activeMarkerKeys);

            await MapInterop.RefreshSizeAsync(ScopeKey);

            if (fit)
            {
                await FitCommandCenterAlertMarkersAsync();
            }
        }

        private async Task PruneCommandCenterAlertMarkersAsync(List<string> activeMarkerKeys)
        {
            try
            {
                var removed = await JS.InvokeAsync<int>(
                    "OutZenInterop.__esm.pruneAntennaAlertMarkers",
                    activeMarkerKeys,
                    ScopeKey);

                Console.WriteLine($"[COMMAND CENTER] Old markers pruned={removed}");
            }
            catch (JSException ex)
            {
                Console.WriteLine(
                    $"[COMMAND CENTER] pruneAntennaAlertMarkers failed: {ex.Message}");
            }
        }

        private async Task PruneAllCommandCenterAlertMarkersAsync()
        {
            try
            {
                var removed = await JS.InvokeAsync<int>(
                    "OutZenInterop.__esm.pruneAntennaAlertMarkers",
                    Array.Empty<string>(),
                    ScopeKey);

                Console.WriteLine($"[COMMAND CENTER] All old markers pruned={removed}");
            }
            catch (JSException ex)
            {
                Console.WriteLine(
                    $"[COMMAND CENTER] prune all failed: {ex.Message}");
            }
        }

        private async Task FitCommandCenterAlertMarkersAsync()
        {
            try
            {
                var fitted = await JS.InvokeAsync<bool>(
                    "OutZenInterop.__esm.fitToAntennaAlertMarkers",
                    ScopeKey,
                    new
                    {
                        maxZoom = 9,
                        padding = new[] { 40, 40 }
                    });

                Console.WriteLine($"[COMMAND CENTER] fitToAntennaAlertMarkers={fitted}");
            }
            catch (JSException ex)
            {
                Console.WriteLine(
                    $"[COMMAND CENTER] fitToAntennaAlertMarkers failed: {ex.Message}");
            }
        }

        private static string BuildMarkerKey(CrowdAlertCluster cluster)
        {
            var alertIds = cluster.AlertIds ?? new List<long>();

            if (alertIds.Count > 0)
            {
                return $"cc:{string.Join("-", alertIds.OrderBy(x => x))}";
            }

            return $"cc:{Math.Round(cluster.Latitude, 4)}:{Math.Round(cluster.Longitude, 4)}";
        }

        private static string BuildAntennaList(CrowdAlertCluster cluster)
        {
            var antennaIds = cluster.AntennaIds ?? new List<int>();

            if (antennaIds.Count == 0)
                return "N/A";

            return string.Join(", ", antennaIds.OrderBy(x => x));
        }

        private static string GetSeverityClass(CrowdAlertCluster cluster)
        {
            if (cluster.RiskScore >= 85 || cluster.Severity >= 4)
                return "is-critical";

            if (cluster.RiskScore >= 65 || cluster.Severity == 3)
                return "is-high";

            if (cluster.RiskScore >= 40 || cluster.Severity == 2)
                return "is-moderate";

            return "is-normal";
        }

        private static string GetOperationalSummary(CrowdAlertCluster cluster)
        {
            if (cluster.RiskScore >= 85 || cluster.Severity >= 4)
                return "Critical crowd concentration. Avoid recommending this zone and prepare alternatives.";

            if (cluster.RiskScore >= 65 || cluster.Severity == 3)
                return "High crowd pressure. Monitor closely and display user warning.";

            if (cluster.RiskScore >= 40 || cluster.Severity == 2)
                return "Moderate anomaly. Keep under observation.";

            return "Situation under control.";
        }

        private static string GetActionPriorityClass(DecisionActionDTO action)
        {
            return action.Priority switch
            {
                "Critical" => "is-critical",
                "High" => "is-high",
                "Moderate" => "is-moderate",
                _ => "is-normal"
            };
        }
        private static bool IsValidCoordinate(double lat, double lng)
        {
            return double.IsFinite(lat)
                   && double.IsFinite(lng)
                   && lat >= 49.45
                   && lat <= 51.6
                   && lng >= 2.3
                   && lng <= 6.6;
        }

        private static void LogInfo(string message)
        {
            Console.WriteLine($"[COMMAND CENTER] {message}");
        }

        private static void LogVerbose(string message)
        {
            if (VerboseCommandCenterLogs)
                Console.WriteLine($"[COMMAND CENTER] {message}");
        }

        private void LogCounterChanges()
        {
            var clusters = TotalClusterCount;
            var fusedAlerts = TotalFusedAlertCount;
            var activeConnections = TotalActiveConnectionCount;
            var uniqueDevices = TotalUniqueDeviceCount;

            if (_lastClusterCount is null)
            {
                _lastClusterCount = clusters;
                _lastFusedAlertCount = fusedAlerts;
                _lastActiveConnectionCount = activeConnections;
                _lastUniqueDeviceCount = uniqueDevices;

                LogInfo(
                    $"Counters baseline: clusters={clusters}, fusedAlerts={fusedAlerts}, activeConnections={activeConnections}, uniqueDevices={uniqueDevices}");

                return;
            }

            var clusterDelta = clusters - _lastClusterCount.Value;
            var alertDelta = fusedAlerts - _lastFusedAlertCount!.Value;
            var connectionDelta = activeConnections - _lastActiveConnectionCount!.Value;
            var deviceDelta = uniqueDevices - _lastUniqueDeviceCount!.Value;

            if (clusterDelta != 0 || alertDelta != 0 || connectionDelta != 0 || deviceDelta != 0)
            {
                LogInfo(
                    $"Counters changed: " +
                    $"clusters {_lastClusterCount}->{clusters} ({clusterDelta:+#;-#;0}), " +
                    $"fusedAlerts {_lastFusedAlertCount}->{fusedAlerts} ({alertDelta:+#;-#;0}), " +
                    $"activeConnections {_lastActiveConnectionCount}->{activeConnections} ({connectionDelta:+#;-#;0}), " +
                    $"uniqueDevices {_lastUniqueDeviceCount}->{uniqueDevices} ({deviceDelta:+#;-#;0})");
            }
            else
            {
                LogVerbose(
                    $"Counters unchanged: clusters={clusters}, fusedAlerts={fusedAlerts}, activeConnections={activeConnections}");
            }

            _lastClusterCount = clusters;
            _lastFusedAlertCount = fusedAlerts;
            _lastActiveConnectionCount = activeConnections;
            _lastUniqueDeviceCount = uniqueDevices;
        }

        private void StartAutoRefresh()
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();

            _refreshCts = new CancellationTokenSource();
            _refreshTask = RefreshLoopAsync(_refreshCts.Token);
        }

        private async Task RefreshLoopAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(RefreshInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    await LoadCommandCenterDataAsync(ct);

                    await InvokeAsync(async () =>
                    {
                        await ReseedAsync(fit: false);
                        StateHasChanged();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Normal when leaving the page.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COMMAND CENTER] Auto-refresh failed: {ex}");
            }
        }

        protected override async Task OnBeforeDisposeAsync()
        {
            try
            {
                _refreshCts?.Cancel();

                if (_refreshTask is not null)
                {
                    await _refreshTask;
                }
            }
            catch
            {
                // Ignore dispose errors.
            }
            finally
            {
                _refreshCts?.Dispose();
                _refreshCts = null;
                _refreshTask = null;
            }
        }
    }
}

























































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.