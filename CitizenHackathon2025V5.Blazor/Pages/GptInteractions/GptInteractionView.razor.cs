using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.VisualBasic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions
{
    public partial class GptInteractionView : IAsyncDisposable
    {
        [Inject] public GptInteractionService GptInteractionService { get; set; } = default!;
        [Inject] public IGptClientOrchestrator GptClientOrchestrator { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;

        protected override string MapId => "leafletMap-gptinteractionview";
        protected override string ScopeKey => "gptinteractionview";
        protected override int DefaultZoom => 14;
        protected override (double lat, double lng) DefaultCenter => (50.29, 4.99);

        /*
         * This page should only retain the markers
         * whose identifier starts with "gpt:".
         */
        protected override OutZenMarkerPolicy MarkerPolicy => OutZenMarkerPolicy.OnlyPrefix;
        protected override string AllowedMarkerPrefix => "gpt:";
        protected override bool PruneForeignMarkersOnMapReady => false;

        /*
         * A GPT page does not use hybrid bundles.
         */
        protected override bool EnableHybrid => false;

        /*
         * Geolocated interactions are few.
         * No cluster is needed for the moment.
         */
        protected override bool EnableCluster => false;
        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => true;

        /*
         * SeedAsync already calls ClearCrowdMarkersAsync().
         * Therefore, we avoid a second cleanup that could
         * occur after the markers are created.
         */
        protected override bool ClearAllOnMapReady => false;

        private readonly List<ClientGptInteractionDTO> _allInteractions = new();
        private readonly HashSet<int> _completedVoiceInteractionIds = new();
        private readonly Dictionary<int, (double Latitude, double Longitude)> _interactionLocations = new();
        protected readonly List<ClientGptInteractionDTO> visibleGptInteractions = new();

        private const int PageSize = 20;
        private int currentIndex;

        private double? _gptLatitude;
        private double? _gptLongitude;

        private List<BrowserVoiceDTO> _availableVoices = new();
        private string _selectedVoiceLang = "fr-FR";
        private string? _selectedVoiceName;
        private string _speechRecognitionLang = "fr-FR";
        private string _mistralResponseLang = "fr-FR";
        private string _ttsLang = "fr-FR";

        private int? _activeGptInteractionId;
        private double _voiceRate = 0.95;
        private double _voicePitch = 1.0;
        private double _voiceVolume = 1.0;

        private const bool PreferAsyncPipeline = true;

        public int SelectedId { get; set; }

        private ElementReference ScrollContainerRef;

        protected string NewPrompt { get; set; } = string.Empty;
        protected string _q = string.Empty;
        protected bool _onlyRecent;

        private bool _autoSendVoicePrompt = true;
        private bool _voiceAutoSendInProgress;
        private string? _lastAutoSentVoicePrompt;

        private bool _voiceOutputEnabled = true;
        private int? _lastSpokenInteractionId;
        private int _detailRevision;

        private bool _disposed;
        private bool _renderQueued;
        private bool _handlersRegistered;
        private bool _isSending;
        private bool _showAiOverlay;

        private DateTime _lastRenderUtc = DateTime.MinValue;
        private DateTime? _gptStartedAtUtc;

        private DateTime? _overlayShownAtUtc;
        private static readonly TimeSpan MinOverlayDuration = TimeSpan.FromSeconds(2);
        private static string GptMarkerId(int id) => $"gpt:{id}";

        private PeriodicTimer? _elapsedTimer;
        private CancellationTokenSource? _elapsedTimerCts;
        protected int _elapsedSeconds;

        private DotNetObjectReference<GptInteractionView>? _dotNetRef;

        private bool _voiceSupported;
        private bool _isListening;
        private string _voiceInterimText = string.Empty;

        private enum AiProcessingState
        {
            Idle = 0,
            Generating = 1,
            Success = 2,
            Error = 3
        }

        private AiProcessingState _aiState = AiProcessingState.Idle;
        protected string? _aiStatusMessage;

        private bool IsAiBusy => _aiState == AiProcessingState.Generating;

        private bool CanEditPrompt => !_disposed && !IsAiBusy && !_isSending;

        private bool CanSend =>
            !_disposed &&
            !IsAiBusy &&
            !_isSending &&
            !string.IsNullOrWhiteSpace(NewPrompt);

        private bool CanUseVoice =>
            !_disposed &&
            !IsAiBusy &&
            !_isSending &&
            _voiceSupported;

        private string VoiceButtonText =>
            !_voiceSupported
                ? "Micro unavailable"
                : _isListening
                    ? "Stop and send"
                    : "Speak to Mistral";

        private string VoiceStatusText =>
            !_voiceSupported
                ? "Voice recognition not supported by this browser."
                : _isListening
                    ? "Listening..."
                    : string.IsNullOrWhiteSpace(_voiceInterimText)
                        ? string.Empty
                        : _voiceInterimText;

        private bool ShowStatusBadge =>
            _aiState is AiProcessingState.Generating
            or AiProcessingState.Success
            or AiProcessingState.Error;

        private bool ShowAiOverlay =>
            _showAiOverlay || IsAiBusy;

        private bool HasGeolocatedInteractions =>
            _allInteractions.Any(x =>
                TryGetInteractionCoordinates(x, out _, out _));

        private string AddChipCssClass =>
            IsAiBusy ? "chip add-chip chip--disabled" : "chip add-chip";

        private string? AddChipHref =>
            IsAiBusy ? null : "/gptinteractioncreate";

        private DateTime RecentCutoffUtc => DateTime.UtcNow.AddHours(-6);

        private async Task SafeRenderAsync(int minDelayMs = 100)
        {
            if (_disposed) return;
            if (_renderQueued) return;

            var elapsed = DateTime.UtcNow - _lastRenderUtc;
            if (elapsed.TotalMilliseconds < minDelayMs)
                return;

            _renderQueued = true;

            try
            {
                _lastRenderUtc = DateTime.UtcNow;
                await InvokeAsync(StateHasChanged);
            }
            finally
            {
                _renderQueued = false;
            }
        }
        private async Task CloseAiOverlay()
        {
            _showAiOverlay = false;

            if (_aiState == AiProcessingState.Error)
                _aiState = AiProcessingState.Idle;

            _aiStatusMessage = null;

            await InvokeAsync(StateHasChanged);
        }

        private string GetSendButtonText() => _aiState switch
        {
            AiProcessingState.Generating => PreferAsyncPipeline ? "Generating (async)..." : "Generating...",
            _ => "Send"
        };

        private string GetStatusBadgeText() => _aiState switch
        {
            AiProcessingState.Generating => PreferAsyncPipeline ? "Generating async" : "Generating",
            AiProcessingState.Success => "Completed",
            AiProcessingState.Error => "Error",
            _ => string.Empty
        };

        private string GetStatusBadgeCssClass() => _aiState switch
        {
            AiProcessingState.Generating => "badge bg-warning text-dark",
            AiProcessingState.Success => "badge bg-success",
            AiProcessingState.Error => "badge bg-danger",
            _ => "badge bg-light text-dark"
        };

        private string GetGlobeCssClass() => _aiState switch
        {
            AiProcessingState.Generating => "oz-gpt-loader-globe--generating",
            AiProcessingState.Success => "oz-gpt-loader-globe--success",
            AiProcessingState.Error => "oz-gpt-loader-globe--error",
            _ => "oz-gpt-loader-globe--idle"
        };

        protected override async Task OnInitializedAsync()
        {
            _gptLatitude = null;
            _gptLongitude = null;

            _dotNetRef = DotNetObjectReference.Create(this);

            //GptClientOrchestrator.InteractionUpdated += OnInteractionUpdatedAsync;
            //GptClientOrchestrator.StatusChanged += OnStatusChangedAsync;
            RegisterHandlersOnce();

            await LoadInteractionsAsync();
            await DetectVoiceSupportAsync();
            await LoadBrowserVoicesAsync();

            try
            {
                await GptClientOrchestrator.EnsureHubAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GPT VIEW] EnsureHubAsync failed: {ex}");
                _aiStatusMessage = "SignalR GPT hub unavailable. Fallback mode only.";
            }
        }

        protected override async Task SeedAsync(bool fit)
        {
            if (_disposed || !IsMapBooted)
                return;

            await MapInterop.EnsureAsync();
            await MapInterop.ClearCrowdMarkersAsync(ScopeKey);

            var added = 0;
            var withoutLocation = 0;
            var failed = 0;

            foreach (var interaction in _allInteractions)
            {
                if (!TryGetInteractionCoordinates(interaction, out _, out _))
                {
                    withoutLocation++;
                    continue;
                }

                var created = await ApplySingleGptMarkerAsync(interaction);

                if (created)
                    added++;
                else
                    failed++;
            }

            Console.WriteLine($"[GPT][Seed] " + $"interactions={_allInteractions.Count}, " + $"markers={added}, " + $"withoutLocation={withoutLocation}, " + $"failed={failed}");

            var state = await JS.InvokeAsync<object>("OutZenInterop.dumpState", ScopeKey);

            Console.WriteLine("[GPT][Seed][State] " + System.Text.Json.JsonSerializer.Serialize(state));

            if (fit && added > 0)
            {
                await MapInterop.RefreshSizeAsync(ScopeKey);
                await MapInterop.FitToDetailsAsync(ScopeKey);
            }
        }
        private async Task LoadBrowserVoicesAsync()
        {
            try
            {
                var voices = await JS.InvokeAsync<BrowserVoiceDTO[]>("gptVoice.loadVoices");
                _availableVoices = voices?
                    .Where(v => !string.IsNullOrWhiteSpace(v.Lang))
                    .OrderBy(v => v.Lang)
                    .ThenBy(v => v.Name)
                    .ToList() ?? new();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GPT VOICE] LoadBrowserVoicesAsync failed: {ex.Message}");
                _availableVoices = new();
            }
        }
        private async Task LoadInteractionsAsync()
        {
            var fetched = await GptInteractionService.GetAllInteractions();

            var source = fetched ?? new List<ClientGptInteractionDTO>();

            var geolocated = source.Where(x => x.Latitude.HasValue && x.Longitude.HasValue).ToList();

            var latest = source.OrderByDescending(x => x.Id).FirstOrDefault();

            Console.WriteLine($"[GPT][Load] " + $"total={source.Count}, " + $"geolocated={geolocated.Count}, " + $"latestId={latest?.Id}, " + $"latestLat={latest?.Latitude}, " + $"latestLng={latest?.Longitude}");

            var interaction16021 = source.FirstOrDefault(x => x.Id == 16021);

            Console.WriteLine($"[GPT][Load][16021] " + $"found={interaction16021 is not null}, " + $"lat={interaction16021?.Latitude}, " + $"lng={interaction16021?.Longitude}");

            _allInteractions.Clear();
            _allInteractions.AddRange(source);

            visibleGptInteractions.Clear();
            currentIndex = 0;

            LoadMoreItems();

            await SafeRenderAsync(250);

            await NotifyDataLoadedAsync(fit: true);
        }

        private async Task<bool> ApplySingleGptMarkerAsync(ClientGptInteractionDTO interaction)
        {
            if (_disposed || !IsMapBooted || interaction is null || interaction.Id <= 0)
            {
                return false;
            }

            if (!TryGetInteractionCoordinates(interaction, out var latitude, out var longitude))
            {
                return false;
            }

            var prompt = interaction.Prompt?.Trim();
            var description = string.IsNullOrWhiteSpace(prompt) ? "GPT interaction" : prompt.Length <= 120 ? prompt : prompt[..120] + "…";
            var markerId = GptMarkerId(interaction.Id);

            try
            {
                await MapInterop.EnsureAsync();

                /*
                 * Direct call to retrieve
                 * the boolean actually returned by JS.
                 */
                var created = await JS.InvokeAsync<bool>("OutZenInterop.addOrUpdateCrowdMarker",
                    markerId,
                    latitude,
                    longitude,
                    1,
                    new
                    {
                        kind = "gpt",
                        title = $"GPT interaction #{interaction.Id}",

                        description,

                        icon = "🤖",

                        /*
                            * Explicitly prevents clustering,
                            * even if it is re-enabled later.
                            */
                        noCluster = true
                    },
                    ScopeKey);

                Console.WriteLine($"[GPT][Marker] " + $"id={markerId}, " + $"lat={latitude}, " + $"lng={longitude}, " + $"created={created}");

                return created;
            }
            catch (JSException ex)
            {
                Console.Error.WriteLine($"[GPT][Marker] JS failure. " + $"id={markerId}, " + $"error={ex.Message}");

                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GPT][Marker] failure. " + $"id={markerId}, " + $"error={ex.Message}");

                return false;
            }
        }
        private async Task ApplyVoiceOptionsAsync()
        {
            _speechRecognitionLang = _selectedVoiceLang == "wa-central" ? "fr-BE" : _selectedVoiceLang;

            _mistralResponseLang = _selectedVoiceLang;

            _ttsLang = _selectedVoiceLang == "wa-central" ? "fr-BE" : _selectedVoiceLang;

            await JS.InvokeVoidAsync("gptVoice.saveVoiceOptions", new
            {
                voiceName = _selectedVoiceName,
                lang = _selectedVoiceLang,
                rate = _voiceRate,
                pitch = _voicePitch,
                volume = _voiceVolume
            });
        }

        private async Task DetectVoiceSupportAsync()
        {
            try
            {
                _voiceSupported = await JS.InvokeAsync<bool>("gptVoice.isSupported");
            }
            catch
            {
                _voiceSupported = false;
            }

            await InvokeAsync(StateHasChanged);
        }

        private async Task CloseGptDetail()
        {
            await ClearDetailMarkerHighlightAsync(
                restoreOverview: true);

            SelectedId = 0;

            await InvokeAsync(StateHasChanged);
        }
        private void LoadMoreItems()
        {
            var next = _allInteractions
                .Skip(currentIndex)
                .Take(PageSize)
                .ToList();

            if (next.Count == 0)
                return;

            visibleGptInteractions.AddRange(next);
            currentIndex += next.Count;
        }

        private async Task ClickInfo(int id)
        {
            if (id <= 0 || IsAiBusy)
                return;

            SelectedId = id;

            /*
             * Display the details first.
             */
            await InvokeAsync(StateHasChanged);

            var interaction = _allInteractions.FirstOrDefault(x => x.Id == id);

            if (interaction is null)
            {
                Console.WriteLine($"[GPT Detail] Interaction #{id} " + $"not found in the local list.");

                return;
            }

            /*
             * A text interaction without a position
             * intentionally has no marker.
             */
            if (!TryGetInteractionCoordinates(interaction, out _, out _))
            {
                Console.WriteLine($"[GPT Detail] Interaction #{id} " + $"has no geographic context. " + $"Detail opened without map highlight.");

                return;
            }

            /*
             * Ensure the marker exists before
             * requesting its highlight.
             */
            var markerCreated = await ApplySingleGptMarkerAsync(interaction);

            if (!markerCreated)
                return;

            await HighlightDetailMarkerAsync(GptMarkerId(id));
        }

        private async Task HandleScroll()
        {
            if (_disposed)
                return;

            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5 && currentIndex < _allInteractions.Count)
            {
                LoadMoreItems();
                await InvokeAsync(StateHasChanged);
            }
        }

        private IEnumerable<ClientGptInteractionDTO> FilterGpt(IEnumerable<ClientGptInteractionDTO> source)
        {
            var q = _q?.Trim();

            return source
                .Where(x =>
                    string.IsNullOrWhiteSpace(q) ||
                    (!string.IsNullOrWhiteSpace(x.Prompt) && x.Prompt.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(x.Response) && x.Response.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !_onlyRecent || x.CreatedAt >= RecentCutoffUtc);
        }

        private async Task HandleAskGpt()
        {
            if (_activeGptInteractionId.HasValue)
            {
                _aiStatusMessage = $"Une génération est déjà en cours #{_activeGptInteractionId}. Attends la fin ou annule-la.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            if (_disposed || _isSending || IsAiBusy)
                return;

            var rawPrompt = NewPrompt?.Trim();
            if (string.IsNullOrWhiteSpace(rawPrompt))
                return;

            var prompt = rawPrompt;

            var languageCode = ResolvePromptLanguage(rawPrompt, _mistralResponseLang);

            _mistralResponseLang = languageCode;

            _ttsLang = languageCode == "wa-central" ? "fr-BE" : languageCode;

            double? effectiveLatitude = null;
            double? effectiveLongitude = null;

            var nearMeIntent = IsNearMePrompt(rawPrompt);

            // Contact details written explicitly
            // in the prompt take priority.
            if (TryExtractCoordinatesFromPrompt(rawPrompt, out var parsedLat, out var parsedLng))
            {
                effectiveLatitude = parsedLat;
                effectiveLongitude = parsedLng;

                Console.WriteLine(
                    $"[GPT VIEW] Coordinates extracted " +
                    $"from prompt: " +
                    $"lat={effectiveLatitude}, " +
                    $"lng={effectiveLongitude}");
            }
            else if (nearMeIntent)
            {
                var gpsAvailable = await TryAcquireGpsForGptAsync();

                if (!gpsAvailable)
                {
                    _aiState = AiProcessingState.Error;

                    _aiStatusMessage =
                        "Votre position n’est pas disponible. " +
                        "Autorisez la géolocalisation pour utiliser « près de moi ».";

                    await SafeRenderAsync();

                    return;
                }

                effectiveLatitude = _gptLatitude;

                effectiveLongitude = _gptLongitude;
            }

            try
            {
                _isSending = true;
                ResetUiState();

                _aiState = AiProcessingState.Generating;
                _aiStatusMessage = PreferAsyncPipeline ? "Submitting async GPT request..." : "Generating response...";

                await StartElapsedTimerAsync();
                await ShowOverlayAsync();
                await InvokeAsync(StateHasChanged);

                if (TryExtractCoordinatesFromPrompt(rawPrompt, out parsedLat, out parsedLng))
                {
                    effectiveLatitude = parsedLat;
                    effectiveLongitude = parsedLng;

                    Console.WriteLine($"[GPT VIEW] Coordinates extracted from prompt: lat={effectiveLatitude}, lng={effectiveLongitude}");
                }

                await GptClientOrchestrator.EnsureHubAsync();

                var started = await GptClientOrchestrator.StartAsync(
                    prompt,
                    latitude: effectiveLatitude,
                    longitude: effectiveLongitude,
                    languageCode: languageCode,
                    ct: CancellationToken.None);

                if (started is null || started.InteractionId <= 0)
                    throw new InvalidOperationException("The GPT request could not be started.");

                _activeGptInteractionId = started.InteractionId;

                if (effectiveLatitude.HasValue &&
                    effectiveLongitude.HasValue &&
                    double.IsFinite(effectiveLatitude.Value) &&
                    double.IsFinite(effectiveLongitude.Value) &&
                    effectiveLatitude.Value is >= -90 and <= 90 &&
                    effectiveLongitude.Value is >= -180 and <= 180 &&
                    !(effectiveLatitude.Value == 0 &&
                      effectiveLongitude.Value == 0))
                {
                    _interactionLocations[started.InteractionId] =
                    (
                        effectiveLatitude.Value,
                        effectiveLongitude.Value
                    );

                    Console.WriteLine($"[GPT][Location] Stored for " + $"interaction #{started.InteractionId}: " + $"{effectiveLatitude.Value}, " + $"{effectiveLongitude.Value}");
                }

                SelectedId = started.InteractionId;
                NewPrompt = string.Empty;

                _aiStatusMessage = "Generation started...";
                await SafeRenderAsync();

                _gptStartedAtUtc = DateTime.UtcNow;

                Console.WriteLine($"[GPT TIMER] START #{started.InteractionId} at {_gptStartedAtUtc:HH:mm:ss.fff}");
            }
            catch (OperationCanceledException)
            {
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = "Generation cancelled.";
                await SafeRenderAsync();

                await Task.Delay(600);
                await StopElapsedTimerAsync();
                await SafeRenderAsync();
            }
            catch (Exception ex)
            {
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = $"Error: {ex.Message}";
                await SafeRenderAsync();

                Console.Error.WriteLine($"[GPT] HandleAskGpt failed: {ex}");

                await Task.Delay(900);
                await StopElapsedTimerAsync();
                await SafeRenderAsync();
            }
            finally
            {
                _isSending = false;

                _activeGptInteractionId = null;
            }
        }

        private async Task OnInteractionUpdatedAsync(ClientGptInteractionDTO dto)
        {
            if (_disposed || dto is null || dto.Id <= 0)
                return;

            UpsertInteraction(_allInteractions, dto);
            UpsertInteraction(visibleGptInteractions, dto);

            if (SelectedId == 0 || SelectedId == dto.Id)
                SelectedId = dto.Id;

            await SafeRenderAsync(700);

            if (IsMapBooted)
            {
                await ApplySingleGptMarkerAsync(dto);
            }
        }

        private bool CanSpeakFinalResponse(ClientGptInteractionDTO dto)
        {
            if (!_voiceOutputEnabled)
                return false;

            if (dto.Id <= 0)
                return false;

            if (_lastSpokenInteractionId == dto.Id)
                return false;

            if (string.IsNullOrWhiteSpace(dto.Response))
                return false;

            if (dto.Response.Contains("Waiting", StringComparison.OrdinalIgnoreCase))
                return false;

            if (dto.Response.Contains("Generating", StringComparison.OrdinalIgnoreCase))
                return false;

            if (dto.Response.StartsWith("GPT request failed", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private bool TryGetInteractionCoordinates(ClientGptInteractionDTO interaction, out double latitude, out double longitude)
        {
            latitude = default;
            longitude = default;

            if (interaction is null || interaction.Id <= 0)
                return false;

            /*
             * Primary source :
             * coordinates persisted and returned by the API.
             */
            if (interaction.Latitude.HasValue && interaction.Longitude.HasValue)
            {
                latitude = interaction.Latitude.Value;
                longitude = interaction.Longitude.Value;
            }
            /*
             * Secondary source :
             * position remembered during the local start
             * of the interaction.
             */
            else if (_interactionLocations.TryGetValue(interaction.Id, out var remembered))
            {
                latitude = remembered.Latitude;
                longitude = remembered.Longitude;
            }
            else
            {
                return false;
            }

            return
                double.IsFinite(latitude) &&
                double.IsFinite(longitude) &&
                latitude is >= -90 and <= 90 &&
                longitude is >= -180 and <= 180 &&
                !(latitude == 0 && longitude == 0);
        }

        private string ResolveTtsLang()
        {
            return _mistralResponseLang switch
            {
                "wa-central" => "fr-FR",
                "fr-BE" => "fr-FR",
                _ => _ttsLang
            };
        }

        private async Task OnStatusChangedAsync(string? message)
        {
            if (_disposed || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var cleanMessage = message.Trim();

            _aiStatusMessage = cleanMessage;

            var isFailed = cleanMessage.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                cleanMessage.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
                cleanMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                cleanMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                cleanMessage.Contains("exceeded", StringComparison.OrdinalIgnoreCase) ||
                cleanMessage.Contains("exception", StringComparison.OrdinalIgnoreCase);

            var isCancelled = cleanMessage.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
                cleanMessage.Contains("canceled", StringComparison.OrdinalIgnoreCase) ||
                cleanMessage.Contains("annulée", StringComparison.OrdinalIgnoreCase) ||
                cleanMessage.Contains("annulee", StringComparison.OrdinalIgnoreCase);

            if (isFailed || isCancelled)
            {
                _activeGptInteractionId = null;
                _isSending = false;

                _aiState = AiProcessingState.Error;

                await StopElapsedTimerAsync();

                if (_gptStartedAtUtc.HasValue)
                {
                    var duration = DateTime.UtcNow - _gptStartedAtUtc.Value;

                    Console.WriteLine($"[GPT TIMER] TERMINATED after " + $"{duration.TotalSeconds:F1}s. " + $"Reason={cleanMessage}");
                }

                _gptStartedAtUtc = null;

                await SafeRenderAsync(100);

                return;
            }

            if (string.Equals(_aiStatusMessage, cleanMessage, StringComparison.Ordinal))
            {
                await SafeRenderAsync(500);
                return;
            }

            await SafeRenderAsync(500);
        }
        private async Task ToggleVoiceAsync()
        {
            if (_disposed || IsAiBusy || _isSending)
                return;

            if (!_voiceSupported)
            {
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = "Voice recognition is not supported by this browser.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            if (_isListening)
            {
                await StopVoiceAsync();
                return;
            }

            try
            {
                var result = await JS.InvokeAsync<VoiceStartResult>("gptVoice.start", _dotNetRef, _speechRecognitionLang);

                Console.WriteLine($"[GPT VOICE] Start result: ok={result?.Ok}, error={result?.Error}");

                if (result is null || !result.Ok)
                {
                    _aiState = AiProcessingState.Error;
                    _aiStatusMessage = string.IsNullOrWhiteSpace(result?.Error) ? "Unable to start voice recognition." : result.Error;

                    _isListening = false;
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                _isListening = true;
                _voiceInterimText = "Listening... speak clearly now.";
                _aiStatusMessage = null;
                await InvokeAsync(StateHasChanged);
            }
            catch (JSException ex)
            {
                _isListening = false;
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = $"Voice error: {ex.Message}";
                await InvokeAsync(StateHasChanged);
            }
        }

        private string BuildPromptWithResponseLanguage(string prompt)
        {
            var languageInstruction = _mistralResponseLang switch
            {
                "fr-FR" or "fr-BE" => "Réponds en français.",
                "en-US" or "en-GB" => "Answer in English.",
                "nl-NL" => "Antwoord in het Nederlands.",
                "de-DE" => "Antworte auf Deutsch.",
                "it-IT" => "Rispondi in italiano.",
                "es-ES" => "Responde en español.",
                "ru-RU" => "Отвечай на русском языке.",
                "zh-CN" => "请用中文回答。",
                "ja-JP" => "日本語で答えてください。",
                _ => "Réponds en français."
            };

            return $"{languageInstruction}\n\nQuestion utilisateur : {prompt}";
        }
        private async Task TrySpeakCompletedInteractionAsync(ClientGptInteractionDTO? dto)
        {
            if (dto is null)
                return;

            Console.WriteLine($"[GPT VOICE] TrySpeak dto={dto.Id}, enabled={_voiceOutputEnabled}, responseLen={dto.Response?.Length ?? 0}");

            if (_disposed || !_voiceOutputEnabled)
                return;

            if (dto is null || dto.Id <= 0)
                return;

            var response = dto.Response?.Trim();

            if (string.IsNullOrWhiteSpace(response))
                return;

            //var parts = Regex.Split(response, @"(?<=[.!?])\s+")
            //    .Where(x => !string.IsNullOrWhiteSpace(x))
            //    .ToList();

            if (response.Length < 80)
            {
                Console.WriteLine($"[GPT VOICE] Skip speak: response too short / probably chunk. id={dto.Id}, len={response.Length}");
                return;
            }

            if (!IsFinalUsableResponse(response))
                return;

            if (_lastSpokenInteractionId == dto.Id)
            {
                Console.WriteLine($"[GPT VOICE] Skip speak: already spoken dto={dto.Id}");
                return;
            }

            if (string.IsNullOrWhiteSpace(dto.Response))
            {
                Console.WriteLine($"[GPT VOICE] Skip speak: empty response for dto={dto.Id}");
                return;
            }

            if (dto.Response.Contains("Waiting", StringComparison.OrdinalIgnoreCase))
                return;

            _lastSpokenInteractionId = dto.Id;

            Console.WriteLine($"[GPT VOICE] Speak full response len={response.Length}");

            var speech = await JS.InvokeAsync<SpeechResult>("gptVoice.speak", response, ResolveTtsLang());

            Console.WriteLine($"[GPT VOICE] speak result ok={speech?.Ok}, error={speech?.Error}");

            //foreach (var part in parts)
            //{
                

            //    if (speech is null || !speech.Ok)
            //        break;
            //}
        }

        private static readonly IReadOnlyList<VoiceLanguageOption> VoiceLanguages =
        [
            new("fr-FR", "Français"),
            new("en-US", "English"),
            new("nl-NL", "Nederlands"),
            new("de-DE", "Deutsch"),
            new("it-IT", "Italiano"),
            new("es-ES", "Español"),
            new("ru-RU", "Русский"),
            new("zh-CN", "中文"),
            new("ja-JP", "日本語"),
            new("wa-central", "Experimental Wallon Central")
        ];

        private sealed record VoiceLanguageOption(string Code, string Label);

        private sealed record VoicePreset(
            string Code,
            string Label,
            string Lang,
            double Rate,
            double Pitch,
            double Volume
        );

        private static readonly IReadOnlyList<VoicePreset> VoicePresets =
        [
            new("jp-samurai", "Samouraï japonais grave", "ja-JP", 0.78, 0.55, 1.0),
            new("jp-calm", "Japonais calme", "ja-JP", 0.90, 0.85, 1.0),
            new("jp-neutral", "Japonais neutre", "ja-JP", 0.95, 1.0, 1.0)
        ];

        private static bool TryExtractCoordinatesFromPrompt(string? prompt, out double latitude, out double longitude)
        {
            latitude = default;
            longitude = default;

            if (string.IsNullOrWhiteSpace(prompt))
                return false;

            // Match: (50.434780,5.876832) ou 50.434780, 5.876832
            var match = Regex.Match(
                prompt,
                @"(?<lat>[+-]?\d{1,2}(?:[.,]\d+)?)\s*[,;]\s*(?<lng>[+-]?\d{1,3}(?:[.,]\d+)?)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            if (!match.Success)
                return false;

            var latText = match.Groups["lat"].Value.Replace(',', '.');
            var lngText = match.Groups["lng"].Value.Replace(',', '.');

            if (!double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude))
                return false;

            if (!double.TryParse(lngText, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
                return false;

            return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
        }

        private static bool IsFinalUsableResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            if (response.Contains("— Waiting", StringComparison.OrdinalIgnoreCase))
                return false;

            if (response.StartsWith("GPT request failed", StringComparison.OrdinalIgnoreCase))
                return false;

            return response.Length >= 80;
        }

        private static bool IsNearMePrompt(string? prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return false;

            var text = prompt.Replace("’", "'").ToLowerInvariant();

            return
                Regex.IsMatch(
                    text,
                    @"\b(près|pres|proche)\s+de\s+moi\b") ||

                Regex.IsMatch(
                    text,
                    @"\bautour\s+de\s+moi\b") ||

                text.Contains("autour de ma position") ||

                text.Contains("près d'ici");
        }

        private async Task<bool>TryAcquireGpsForGptAsync()
        {
            try
            {
                var position = await JS.InvokeAsync<BrowserGpsPosition>("OutZen.getCurrentPositionForGpt");

                if (position is null ||
                    !double.IsFinite(
                        position.Latitude) ||
                    !double.IsFinite(
                        position.Longitude) ||
                    position.Latitude is < -90 or > 90 ||
                    position.Longitude is < -180 or > 180)
                {
                    return false;
                }

                _gptLatitude =
                    position.Latitude;

                _gptLongitude =
                    position.Longitude;

                Console.WriteLine(
                    $"[GPT GPS] Resolved for request. " +
                    $"lat={_gptLatitude}, " +
                    $"lng={_gptLongitude}, " +
                    $"accuracy={position.Accuracy}m");

                return true;
            }
            catch (JSException ex)
            {
                Console.Error.WriteLine(
                    $"[GPT GPS] Failed: {ex.Message}");

                _gptLatitude = null;
                _gptLongitude = null;

                return false;
            }
        }

        private sealed class SpeechResult
        {
            public bool Ok { get; set; }
            public string? Error { get; set; }
        }

        private sealed class VoiceMicrophoneTestResult
        {
            public bool Ok { get; set; }
            public string? Error { get; set; }
        }

        private sealed class BrowserGpsPosition
        {
            public double Latitude { get; set; }

            public double Longitude { get; set; }

            public double Accuracy { get; set; }
        }

        private async Task StopVoiceAsync()
        {
            try
            {
                await JS.InvokeVoidAsync("gptVoice.stop");
            }
            catch
            {
            }

            _isListening = false;
            _voiceInterimText = string.Empty;
            await InvokeAsync(StateHasChanged);
        }

        private async Task StopSpeakingAsync()
        {
            try
            {
                await JS.InvokeVoidAsync("gptVoice.stopSpeaking");
            }
            catch
            {
            }
        }

        [JSInvokable]
        public async Task OnVoiceRecognitionResult(string finalText, string interimText)
        {
            if (_disposed)
                return;

            if (!string.IsNullOrWhiteSpace(finalText))
            {
                var clean = finalText.Trim();

                if (string.IsNullOrWhiteSpace(NewPrompt))
                {
                    NewPrompt = clean;
                }
                else
                {
                    NewPrompt = $"{NewPrompt.Trim()} {clean}".Trim();
                }

                _voiceInterimText =
                    "Phrase capturée. " +
                    "Continuez à parler ou appuyez sur " +
                    "« Stop and send ».";
            }
            else if (!string.IsNullOrWhiteSpace(interimText))
            {
                _voiceInterimText = interimText.Trim();
            }
            else
            {
                _voiceInterimText = "Écoute en cours…";
            }

            await SafeRenderAsync();

            Console.WriteLine($"[GPT VOICE] final='{finalText}', " + $"interim='{interimText}'");
        }

        [JSInvokable]
        public async Task OnVoiceRecognitionError(string errorMessage)
        {
            if (_disposed)
                return;

            var error = errorMessage?.Trim() ?? "unknown";

            /*
             * Additional security :
             * a pause should not stop the dictation.
             */
            if (error.Contains("No speech", StringComparison.OrdinalIgnoreCase) || error.Contains("no-speech", StringComparison.OrdinalIgnoreCase))
            {
                _isListening = true;

                _voiceInterimText = "Pause détectée. " + "OutZen continue de vous écouter…";

                _aiStatusMessage = null;

                await SafeRenderAsync();

                return;
            }

            if (error.Contains("aborted", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _isListening = false;
            _voiceInterimText = $"Erreur du microphone : {error}";

            _aiState = AiProcessingState.Error;

            _aiStatusMessage = error;

            await SafeRenderAsync();
        }

        [JSInvokable]
        public async Task OnVoiceStopped()
        {
            if (_disposed)
                return;

            _isListening = false;

            var prompt = NewPrompt?.Trim();

            if (string.IsNullOrWhiteSpace(prompt))
            {
                _voiceInterimText = "Dictation stopped. No final text received.";
                await SafeRenderAsync();
                Console.WriteLine("[GPT VOICE] stopped without final prompt");
                return;
            }

            _voiceInterimText = "Dictation stopped. Sending prompt...";
            await SafeRenderAsync();

            Console.WriteLine("[GPT VOICE] stopped");

            if (!_autoSendVoicePrompt)
                return;

            if (_voiceAutoSendInProgress || _isSending || IsAiBusy)
                return;

            if (string.Equals(_lastAutoSentVoicePrompt, prompt, StringComparison.Ordinal))
                return;

            try
            {
                _voiceAutoSendInProgress = true;
                _lastAutoSentVoicePrompt = prompt;

                NewPrompt = NormalizeVoicePrompt(prompt);

                await HandleAskGpt();
            }
            finally
            {
                _voiceAutoSendInProgress = false;
            }
        }

        private static string NormalizeVoicePrompt(string prompt)
        {
            return prompt
                .Replace("autour d'un ami", "autour de Namur", StringComparison.OrdinalIgnoreCase)
                .Replace("autour de N'amur", "autour de Namur", StringComparison.OrdinalIgnoreCase)
                .Replace("autour d Namur", "autour de Namur", StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        private static string ResolvePromptLanguage(string prompt, string fallbackLanguage)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return string.IsNullOrWhiteSpace(fallbackLanguage) ? "fr-FR" : fallbackLanguage;
            }

            /*
             * Cyrillic alphabet :
             * Russian, Ukrainian, Bulgarian, etc.
             */
            if (Regex.IsMatch(prompt, @"[\u0400-\u04FF]"))
            {
                return "ru-RU";
            }

            /*
             * Alphabet arabe.
             */
            if (Regex.IsMatch(prompt, @"[\u0600-\u06FF]"))
            {
                return "ar-SA";
            }

            /*
             * Hiragana and Katakana.
             */
            if (Regex.IsMatch(prompt, @"[\u3040-\u30FF]"))
            {
                return "ja-JP";
            }

            /*
             * Chinese ideograms.
             */
            if (Regex.IsMatch(prompt, @"[\u4E00-\u9FFF]"))
            {
                return "zh-CN";
            }

            return string.IsNullOrWhiteSpace(fallbackLanguage) ? "fr-FR" : fallbackLanguage;
        }

        private async Task ShowOverlayAsync()
        {
            _showAiOverlay = true;
            _overlayShownAtUtc = DateTime.UtcNow;

            await InvokeAsync(StateHasChanged);
            await Task.Yield();
            await Task.Delay(50);
        }

        private async Task HideOverlayAsync()
        {
            if (_overlayShownAtUtc.HasValue)
            {
                var elapsed = DateTime.UtcNow - _overlayShownAtUtc.Value;
                var remaining = MinOverlayDuration - elapsed;

                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining);
            }

            _showAiOverlay = false;
            _overlayShownAtUtc = null;
            await InvokeAsync(StateHasChanged);
        }

        private async Task StartElapsedTimerAsync()
        {
            await StopElapsedTimerAsync();

            _elapsedSeconds = 0;
            _elapsedTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _elapsedTimerCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    while (_elapsedTimer is not null &&
                           _elapsedTimerCts is not null &&
                           await _elapsedTimer.WaitForNextTickAsync(_elapsedTimerCts.Token))
                    {
                        _elapsedSeconds++;
                        await SafeRenderAsync(500);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                }
            });
        }

        private async Task OnInteractionCompletedAsync(ClientGptInteractionDTO dto)
        {
            if (_disposed || dto is null || dto.Id <= 0)
                return;

            _completedVoiceInteractionIds.Add(dto.Id);

            ClientGptInteractionDTO completed = dto;

            try
            {
                var persisted = await GptInteractionService.GetByIdAsync(dto.Id);

                if (persisted is not null)
                {
                    completed = persisted;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GPT COMPLETE] " + $"Reload #{dto.Id} failed: " + $"{ex.Message}");
            }

            await OnInteractionUpdatedAsync(completed);

            if (SelectedId == completed.Id)
            {
                _detailRevision++;

                await InvokeAsync(StateHasChanged);
            }

            if (IsMapBooted)
            {
                var markerCreated = await ApplySingleGptMarkerAsync(completed);

                if (markerCreated)
                {
                    await MapInterop.RefreshSizeAsync(ScopeKey);

                    await Task.Delay(120);

                    var highlighted = await HighlightDetailMarkerAsync(markerId:GptMarkerId(completed.Id), targetZoom: 16, openPopup: false, verticalOffsetPx: 170);

                    Console.WriteLine(
                        $"[GPT COMPLETE][Map] " +
                        $"id={completed.Id}, " +
                        $"lat={completed.Latitude}, " +
                        $"lng={completed.Longitude}, " +
                        $"markerCreated={markerCreated}, " +
                        $"highlighted={highlighted}");
                }
                else
                {
                    Console.WriteLine(
                        $"[GPT COMPLETE][Map] " +
                        $"No geographic marker for " +
                        $"interaction #{completed.Id}. " +
                        $"lat={completed.Latitude}, " +
                        $"lng={completed.Longitude}");
                }
            }

            if (IsFinalUsableResponse(completed.Response ?? string.Empty))
                await TrySpeakCompletedInteractionAsync(completed);

            _activeGptInteractionId = null;
            _isSending = false;
            _aiState = AiProcessingState.Idle;
            _aiStatusMessage = null;

            await StopElapsedTimerAsync();
            await SafeRenderAsync();

            if (_gptStartedAtUtc.HasValue)
            {
                var duration = DateTime.UtcNow - _gptStartedAtUtc.Value;

                Console.WriteLine($"[GPT TIMER] COMPLETED " + $"#{completed.Id} after " + $"{duration.TotalSeconds:F1}s");
            }

            //if (SelectedId == dto.Id)
            //{
            //    _detailRevision++;

            //    await InvokeAsync(StateHasChanged);
            //}

            _gptStartedAtUtc = null;
        }

        private async Task StopElapsedTimerAsync()
        {
            try
            {
                _elapsedTimerCts?.Cancel();
            }
            catch
            {
            }

            _elapsedTimerCts?.Dispose();
            _elapsedTimerCts = null;

            _elapsedTimer?.Dispose();
            _elapsedTimer = null;

            if (_showAiOverlay)
                await HideOverlayAsync();
        }

        private void ResetUiState()
        {
            _elapsedSeconds = 0;
        }

        private void RegisterHandlersOnce()
        {
            if (_handlersRegistered)
                return;

            _handlersRegistered = true;

            GptClientOrchestrator.InteractionUpdated += OnInteractionUpdatedAsync;
            GptClientOrchestrator.StatusChanged += OnStatusChangedAsync;
            GptClientOrchestrator.InteractionCompleted += OnInteractionCompletedAsync;
        }

        private static void UpsertInteraction(List<ClientGptInteractionDTO> list, ClientGptInteractionDTO dto)
        {
            var index = list.FindIndex(x => x.Id == dto.Id);

            if (index >= 0)
                list[index] = dto;
            else
                list.Insert(0, dto);
        }

        private void ToggleRecent() => _onlyRecent = !_onlyRecent;

        private void ToggleVoiceOutput()
        {
            _voiceOutputEnabled = !_voiceOutputEnabled;
        }

        private Task PreventAddNavigationWhenBusy(MouseEventArgs _) => Task.CompletedTask;

        protected override async Task OnBeforeDisposeAsync()
        {
            try
            {
                await ClearDetailMarkerHighlightAsync(
                    restoreOverview: false);
            }
            catch
            {
            }
            _disposed = true;

            GptClientOrchestrator.InteractionUpdated -= OnInteractionUpdatedAsync;
            GptClientOrchestrator.StatusChanged -= OnStatusChangedAsync;
            GptClientOrchestrator.InteractionCompleted -= OnInteractionCompletedAsync;

            try { await GptClientOrchestrator.CancelCurrentAsync(); } catch { }

            await StopElapsedTimerAsync();

            if (_isListening)
                await StopVoiceAsync();

            _dotNetRef?.Dispose();
            _dotNetRef = null;
        }

        private sealed class VoiceStartResult
        {
            public bool Ok { get; set; }
            public string? Error { get; set; }
        }

        private sealed class BrowserVoiceDTO
        {
            public string? Name { get; set; }
            public string? Lang { get; set; }
            public string? VoiceURI { get; set; }
            public bool LocalService { get; set; }
            public bool Default { get; set; }
        }
    }
}




















































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




