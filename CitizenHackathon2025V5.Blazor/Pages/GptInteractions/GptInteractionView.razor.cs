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
        protected override OutZenMarkerPolicy MarkerPolicy => OutZenMarkerPolicy.OnlyPrefix;
        protected override bool ClearAllOnMapReady => true;

        public List<ClientGptInteractionDTO> GptInteractions { get; set; } = new();

        private List<ClientGptInteractionDTO> _interactions = [];
        private readonly List<ClientGptInteractionDTO> allGptInteractions = new();
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

        private bool _disposed;
        private bool _renderQueued;
        private bool _handlersRegistered;
        private bool _isSending;
        private bool _showAiOverlay;
        private DateTime _lastRenderUtc = DateTime.MinValue;

        private DateTime? _overlayShownAtUtc;
        private static readonly TimeSpan MinOverlayDuration = TimeSpan.FromSeconds(2);

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
            _gptLatitude = DefaultCenter.lat;
            _gptLongitude = DefaultCenter.lng;

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
            GptInteractions = fetched?.ToList() ?? new List<ClientGptInteractionDTO>();

            allGptInteractions.Clear();
            allGptInteractions.AddRange(GptInteractions);

            visibleGptInteractions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            await InvokeAsync(StateHasChanged);
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

        private void LoadMoreItems()
        {
            var next = allGptInteractions
                .Skip(currentIndex)
                .Take(PageSize)
                .ToList();

            if (next.Count == 0)
                return;

            visibleGptInteractions.AddRange(next);
            currentIndex += next.Count;
        }

        private void ClickInfo(int id) => SelectedId = id;

        private async Task HandleScroll()
        {
            if (_disposed)
                return;

            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5 &&
                currentIndex < allGptInteractions.Count)
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
            if (_disposed || _isSending || IsAiBusy)
                return;

            var rawPrompt = NewPrompt?.Trim();
            if (string.IsNullOrWhiteSpace(rawPrompt))
                return;

            var prompt = rawPrompt;
            var languageCode = _selectedVoiceLang;

            try
            {
                _isSending = true;
                ResetUiState();

                _aiState = AiProcessingState.Generating;
                _aiStatusMessage = PreferAsyncPipeline ? "Submitting async GPT request..." : "Generating response...";

                await StartElapsedTimerAsync();
                await ShowOverlayAsync();
                await InvokeAsync(StateHasChanged);

                var effectiveLatitude = _gptLatitude;
                var effectiveLongitude = _gptLongitude;

                if (TryExtractCoordinatesFromPrompt(rawPrompt, out var parsedLat, out var parsedLng))
                {
                    effectiveLatitude = parsedLat;
                    effectiveLongitude = parsedLng;

                    Console.WriteLine(
                        $"[GPT VIEW] Coordinates extracted from prompt: lat={effectiveLatitude}, lng={effectiveLongitude}");
                }

                var result = await GptClientOrchestrator.RunAsync(prompt, latitude: effectiveLatitude, longitude: effectiveLongitude, languageCode: _mistralResponseLang, ct: CancellationToken.None);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);

                    var latest =
                        await GptInteractionService.GetAllInteractions();

                    await InvokeAsync(() =>
                    {
                        _interactions = latest.ToList();
                        StateHasChanged();
                    });
                });

                if (!result.Started)
                    throw new InvalidOperationException("The GPT request could not be started.");

                ClientGptInteractionDTO? finalToSpeak = null;

                if (result.FinalInteraction is not null)
                {
                    SelectedId = result.FinalInteraction.Id;
                    NewPrompt = string.Empty;
                    finalToSpeak = result.FinalInteraction;
                }
                else if (result.PendingInteraction is not null)
                {
                    SelectedId = result.PendingInteraction.Id;
                    NewPrompt = string.Empty;

                    //await Task.Delay(300);

                    //finalToSpeak = await GptInteractionService.GetByIdAsync(result.PendingInteraction.Id);
                }

                _aiState = AiProcessingState.Success;
                _aiStatusMessage = result.StatusMessage ?? "Response generated successfully.";
                await SafeRenderAsync();

                if (finalToSpeak is not null)
                    await TrySpeakCompletedInteractionAsync(finalToSpeak);

                await Task.Delay(600);
                await StopElapsedTimerAsync();

                _aiState = AiProcessingState.Idle;
                _aiStatusMessage = null;
                await SafeRenderAsync();
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
            }
        }

        private async Task OnInteractionUpdatedAsync(ClientGptInteractionDTO dto)
        {
            UpsertInteraction(GptInteractions, dto);
            UpsertInteraction(allGptInteractions, dto);
            UpsertInteraction(visibleGptInteractions, dto);

            if (SelectedId == 0)
                SelectedId = dto.Id;

            await SafeRenderAsync();

            if (!_isSending &&
                _voiceOutputEnabled &&
                dto.Id > 0 &&
                dto.Id != _lastSpokenInteractionId &&
                !string.IsNullOrWhiteSpace(dto.Response) &&
                !dto.Response.Contains("Waiting", StringComparison.OrdinalIgnoreCase))
            {
                await TrySpeakCompletedInteractionAsync(dto);
            }
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

        private Task OnStatusChangedAsync(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                _aiStatusMessage = message;

            return SafeRenderAsync();
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
                var result = await JS.InvokeAsync<VoiceStartResult>(
                    "gptVoice.start",
                    _dotNetRef,
                     _speechRecognitionLang);

                Console.WriteLine($"[GPT VOICE] Start result: ok={result?.Ok}, error={result?.Error}");

                if (result is null || !result.Ok)
                {
                    _aiState = AiProcessingState.Error;
                    _aiStatusMessage = string.IsNullOrWhiteSpace(result?.Error)
                        ? "Unable to start voice recognition."
                        : result.Error;

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

            if (dto.Id <= 0)
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

            var speech = await JS.InvokeAsync<SpeechResult>(
                "gptVoice.speak",
                dto.Response,
                ResolveTtsLang());

            Console.WriteLine($"[GPT VOICE] speak result ok={speech?.Ok}, error={speech?.Error}");
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

            return latitude is >= -90 and <= 90 &&
                   longitude is >= -180 and <= 180;
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
                    NewPrompt = clean;
                else
                    NewPrompt = $"{NewPrompt.Trim()} {clean}".Trim();

                _voiceInterimText = "Voice captured. Sending...";
            }
            else
            {
                _voiceInterimText = interimText ?? string.Empty;
            }

            await SafeRenderAsync();

            Console.WriteLine($"[GPT VOICE] final='{finalText}', interim='{interimText}'");
        }

        [JSInvokable]
        public async Task OnVoiceRecognitionError(string errorMessage)
        {
            if (_disposed)
                return;

            if (!string.IsNullOrWhiteSpace(errorMessage) &&
                errorMessage.Contains("aborted", StringComparison.OrdinalIgnoreCase))
            {
                _isListening = false;
                _voiceInterimText = "Dictation stopped.";
                await SafeRenderAsync();
                return;
            }

            if (errorMessage.Contains("No speech", StringComparison.OrdinalIgnoreCase))
            {
                _isListening = false;
                _voiceInterimText = errorMessage;
                _aiStatusMessage = null;
                await SafeRenderAsync();
                return;
            }
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
            _disposed = true;

            GptClientOrchestrator.InteractionUpdated -= OnInteractionUpdatedAsync;
            GptClientOrchestrator.StatusChanged -= OnStatusChangedAsync;

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




