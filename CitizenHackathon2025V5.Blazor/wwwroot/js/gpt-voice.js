// wwwroot/js/gpt-voice.js
(() => {
    "use strict";

    const SpeechRecognition =
        window.SpeechRecognition || window.webkitSpeechRecognition || null;

    const state = {
        recognition: null,
        isListening: false,
        dotNetRef: null
    };

    function buildRecognition(lang) {
        if (!SpeechRecognition) {
            return null;
        }

        const recognition = new SpeechRecognition();
        recognition.lang = lang || "fr-BE";
        recognition.continuous = false;
        recognition.interimResults = true;
        recognition.maxAlternatives = 1;

        return recognition;
    }

    function ensureSupported() {
        return !!SpeechRecognition;
    }

    async function stopInternal(notifyStopped) {
        if (!state.recognition) {
            state.isListening = false;
            return;
        }

        try {
            state.recognition.onresult = null;
            state.recognition.onerror = null;
            state.recognition.onend = null;
            state.recognition.stop();
        } catch {
            // ignore
        }

        state.recognition = null;
        state.isListening = false;

        if (notifyStopped && state.dotNetRef) {
            try {
                await state.dotNetRef.invokeMethodAsync("OnVoiceStopped");
            } catch {
                // ignore
            }
        }
    }

    window.gptVoice = {
        isSupported() {
            return ensureSupported();
        },

        isListening() {
            return state.isListening;
        },

        async start(dotNetRef, lang) {
            if (!ensureSupported()) {
                return {
                    ok: false,
                    error: "Speech recognition is not supported by this browser."
                };
            }

            if (!dotNetRef) {
                return {
                    ok: false,
                    error: "Missing .NET callback reference."
                };
            }

            if (state.isListening) {
                return {
                    ok: true
                };
            }

            state.dotNetRef = dotNetRef;
            state.recognition = buildRecognition(lang);

            if (!state.recognition) {
                return {
                    ok: false,
                    error: "Speech recognition could not be initialized."
                };
            }

            state.recognition.onresult = async (event) => {
                try {
                    let finalText = "";
                    let interimText = "";

                    for (let i = event.resultIndex; i < event.results.length; i++) {
                        const result = event.results[i];
                        const transcript = result?.[0]?.transcript ?? "";

                        if (result.isFinal) {
                            finalText += transcript;
                        } else {
                            interimText += transcript;
                        }
                    }

                    if (state.dotNetRef) {
                        await state.dotNetRef.invokeMethodAsync(
                            "OnVoiceRecognitionResult",
                            finalText || "",
                            interimText || ""
                        );
                    }
                } catch {
                    // ignore
                }
            };

            state.recognition.onerror = async (event) => {
                state.isListening = false;

                let message = "Voice recognition error.";
                const code = event?.error || "";

                switch (code) {
                    case "not-allowed":
                        message = "Microphone access was denied.";
                        break;
                    case "audio-capture":
                        message = "No microphone was detected.";
                        break;
                    case "network":
                        message = "A network error occurred during voice recognition.";
                        break;
                    case "no-speech":
                        message = "No speech was detected.";
                        break;
                    case "aborted":
                        message = "Voice recognition was cancelled.";
                        break;
                }

                if (state.dotNetRef) {
                    try {
                        await state.dotNetRef.invokeMethodAsync("OnVoiceRecognitionError", message);
                    } catch {
                        // ignore
                    }
                }
            };

            state.recognition.onend = async () => {
                state.recognition = null;
                state.isListening = false;

                if (state.dotNetRef) {
                    try {
                        await state.dotNetRef.invokeMethodAsync("OnVoiceStopped");
                    } catch {
                        // ignore
                    }
                }
            };

            try {
                state.recognition.start();
                state.isListening = true;

                return {
                    ok: true
                };
            } catch (error) {
                state.recognition = null;
                state.isListening = false;

                return {
                    ok: false,
                    error: error?.message || "Failed to start voice recognition."
                };
            }
        },

        async stop() {
            await stopInternal(true);
            return { ok: true };
        }
    };
})();










































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/