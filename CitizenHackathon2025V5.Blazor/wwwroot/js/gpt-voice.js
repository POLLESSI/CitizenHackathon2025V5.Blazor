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
        /*recognition.lang = lang || "fr-BE";*/
        recognition.lang = "fr-FR";
        /*recognition.lang = "en-US";*/
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
            state.recognition.abort();
        } catch {
        }

        state.recognition = null;
        state.isListening = false;

        if (notifyStopped && state.dotNetRef) {
            try {
                await state.dotNetRef.invokeMethodAsync("OnVoiceStopped");
            } catch {
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

        async testMicrophone() {
            try {
                if (!navigator.mediaDevices?.getUserMedia) {
                    return {
                        ok: false,
                        error: "navigator.mediaDevices.getUserMedia is unavailable."
                    };
                }

                const stream = await navigator.mediaDevices.getUserMedia({ audio: true });

                const tracks = stream.getAudioTracks().map(t => ({
                    label: t.label,
                    enabled: t.enabled,
                    readyState: t.readyState
                }));

                stream.getTracks().forEach(t => t.stop());

                console.log("[gptVoice] microphone test OK", tracks);

                return {
                    ok: true,
                    error: null,
                    tracks
                };
            } catch (e) {
                console.error("[gptVoice] microphone test failed", e);

                return {
                    ok: false,
                    error: e?.message || String(e),
                    tracks: []
                };
            }
        },

        async start(dotNetRef, lang) {
            console.log("[gptVoice] start called", { lang });

            if (!ensureSupported()) {
                return {
                    ok: false,
                    error: "Speech recognition is not supported by this browser."
                };
            }

            if (!window.isSecureContext) {
                return {
                    ok: false,
                    error: "Speech recognition requires HTTPS or localhost."
                };
            }

            if (!dotNetRef) {
                return {
                    ok: false,
                    error: "Missing .NET callback reference."
                };
            }

            // IMPORTANT: Properly terminate all old sessions
            if (state.recognition) {
                try {
                    state.recognition.onresult = null;
                    state.recognition.onerror = null;
                    state.recognition.onend = null;
                    state.recognition.abort();
                } catch {
                }

                state.recognition = null;
                state.isListening = false;

                await new Promise(resolve => setTimeout(resolve, 250));
            }

            state.dotNetRef = dotNetRef;
            state.recognition = buildRecognition(lang);

            const recognition = state.recognition;

            recognition.onstart = () => {
                state.isListening = true;
                console.log("[gptVoice] onstart");
            };

            recognition.onaudiostart = () => {
                console.log("[gptVoice] onaudiostart");
            };

            recognition.onsoundstart = () => {
                console.log("[gptVoice] onsoundstart");
            };

            recognition.onspeechstart = () => {
                console.log("[gptVoice] onspeechstart");
            };

            recognition.onresult = async (event) => {
                let finalText = "";
                let interimText = "";

                for (let i = event.resultIndex; i < event.results.length; i++) {
                    const result = event.results[i];
                    const transcript = result?.[0]?.transcript ?? "";

                    if (result.isFinal)
                        finalText += transcript;
                    else
                        interimText += transcript;
                }

                console.log("[gptVoice] onresult", { finalText, interimText });

                if (state.dotNetRef) {
                    await state.dotNetRef.invokeMethodAsync(
                        "OnVoiceRecognitionResult",
                        finalText || "",
                        interimText || ""
                    );
                }
            };

            recognition.onerror = async (event) => {
                const code = event?.error || "";
                console.warn("[gptVoice] onerror", code, event);

                state.isListening = false;

                if (code === "aborted") {
                    console.warn("[gptVoice] aborted ignored");
                    return;
                }

                if (code === "no-speech") {
                    if (state.dotNetRef) {
                        await state.dotNetRef.invokeMethodAsync(
                            "OnVoiceRecognitionError",
                            "No speech detected. Click Start dictation and speak immediately."
                        );
                    }
                    return;
                }

                let message = `Voice recognition error: ${code || "unknown"}`;

                if (code === "not-allowed")
                    message = "Microphone access was denied.";
                else if (code === "audio-capture")
                    message = "No microphone was detected.";
                else if (code === "network")
                    message = "A network error occurred during voice recognition.";

                if (state.dotNetRef) {
                    await state.dotNetRef.invokeMethodAsync("OnVoiceRecognitionError", message);
                }
            };

            recognition.onend = async () => {
                console.log("[gptVoice] onend");

                state.recognition = null;
                state.isListening = false;

                if (state.suppressNextEnd) {
                    state.suppressNextEnd = false;
                    return;
                }

                if (state.dotNetRef) {
                    await state.dotNetRef.invokeMethodAsync("OnVoiceStopped");
                }
            };

            try {
                recognition.start();
                return { ok: true };
            } catch (error) {
                state.recognition = null;
                state.isListening = false;

                return {
                    ok: false,
                    error: error?.message || "Failed to start voice recognition."
                };
            }
        },

        isSpeechSynthesisSupported() {
            return "speechSynthesis" in window && "SpeechSynthesisUtterance" in window;
        },

        stopSpeaking() {
            if ("speechSynthesis" in window) {
                window.speechSynthesis.cancel();
            }

            return { ok: true };
        },

        speak(text, lang) {
            try {
                if (!text || !text.trim()) {
                    return { ok: false, error: "No text to speak." };
                }

                if (!("speechSynthesis" in window) || !("SpeechSynthesisUtterance" in window)) {
                    return { ok: false, error: "Speech synthesis is not supported." };
                }

                window.speechSynthesis.cancel();

                const parts = text
                    .trim()
                    .replace(/\s+/g, " ")
                    .match(/[^.!?]+[.!?]+|[^.!?]+$/g) || [text];

                let index = 0;

                const speakNext = () => {
                    if (index >= parts.length) {
                        console.log("[gptVoice] speech synthesis completed");
                        return;
                    }

                    const utterance = new SpeechSynthesisUtterance(parts[index].trim());
                    utterance.lang = lang || "fr-FR";
                    utterance.rate = 0.95;
                    utterance.pitch = 1.0;
                    utterance.volume = 1.0;

                    const voices = window.speechSynthesis.getVoices();
                    utterance.voice =
                        voices.find(v => v.lang === "fr-FR") ||
                        voices.find(v => v.lang?.startsWith("fr")) ||
                        null;

                    utterance.onend = () => {
                        index++;
                        setTimeout(speakNext, 120);
                    };

                    utterance.onerror = e => {
                        console.warn("[gptVoice] speech synthesis error", e);
                        index++;
                        setTimeout(speakNext, 120);
                    };

                    window.speechSynthesis.speak(utterance);
                };

                speakNext();

                return { ok: true, error: null };
            } catch (e) {
                return { ok: false, error: e?.message || String(e) };
            }
        },

        async stop() {
            await stopInternal(true);
            return { ok: true };
        }
    };
})();










































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/