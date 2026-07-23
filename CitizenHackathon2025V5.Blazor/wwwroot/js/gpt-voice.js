// wwwroot/js/gpt-voice.js
(() => {
    "use strict";

    const SpeechRecognition =
        window.SpeechRecognition || window.webkitSpeechRecognition || null;

    const state = {
        recognition: null,
        isListening: false,
        dotNetRef: null,

        selectedVoiceName: null,
        selectedLang: "fr-FR",
        rate: 0.95,
        pitch: 1.0,
        volume: 1.0,

        /*
         * Remains true as long as the user
         * has not explicitly requested a stop.
         */
        keepListening: false,

        /*
         * Allows differentiating a natural pause
         * from a click on "Stop and send".
         */
        manualStopRequested: false,

        stopNotificationSent: false,
        restartTimer: null,
        sessionId: 0
    };

    function buildRecognition(lang)
    {
        if (!SpeechRecognition) {
            return null;
        }

        const recognition = new SpeechRecognition();
        /*recognition.lang = lang || "fr-BE";*/
        recognition.lang = lang || "fr-FR";
        /*recognition.lang = "en-US";*/
        recognition.continuous = true;
        recognition.interimResults = true;
        recognition.maxAlternatives = 1;

        return recognition;
    }

    function ensureSupported() {
        return !!SpeechRecognition;
    }

    function clearRestartTimer() {
        if (state.restartTimer !== null) {
            window.clearTimeout(state.restartTimer);
            state.restartTimer = null;
        }
    }

    async function notifyStoppedOnce() {
        if (state.stopNotificationSent) {
            return;
        }

        state.stopNotificationSent = true;

        console.log("[gptVoice] explicit stop notification");

        if (!state.dotNetRef) {
            return;
        }

        try {
            await state.dotNetRef.invokeMethodAsync("OnVoiceStopped");
        }
        catch (error) {
            console.warn("[gptVoice] OnVoiceStopped failed", error);
        }
    }

    async function stopInternal(notifyStopped) {
        clearRestartTimer();

        state.keepListening = false;
        state.manualStopRequested = notifyStopped === true;

        const recognition = state.recognition;

        if (!recognition) {
            state.isListening = false;

            if (notifyStopped) {
                await notifyStoppedOnce();
            }

            return;
        }

        /*
         * stop() allows the engine to provide
         * a final result.
         *
         * abort() would potentially discard
         * the last spoken words.
         */
        try {
            recognition.stop();
        }
        catch (error) {
            console.warn(
                "[gptVoice] recognition.stop failed",
                error);

            state.recognition = null;
            state.isListening = false;

            if (notifyStopped) {
                await notifyStoppedOnce();
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
                state.keepListening = false;
                state.manualStopRequested = false;
                clearRestartTimer();
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

            clearRestartTimer();

            state.dotNetRef = dotNetRef;

            state.sessionId++;
            const currentSessionId = state.sessionId;

            state.keepListening = true;
            state.manualStopRequested = false;
            state.stopNotificationSent = false;
            state.isListening = false;

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

            recognition.onerror = async event => {const code = event?.error || "unknown";

                console.warn("[gptVoice] onerror",
                {
                    code,
                    keepListening:
                        state.keepListening,
                    manualStopRequested:
                        state.manualStopRequested
                });

                /*
                 * A temporary loss of speech
                 * usually corresponds to a pause.
                 *
                 * onend will restart the recognition.
                 */
                if (code === "no-speech") {
                    console.log("[gptVoice] no-speech ignored; " + "listening will restart");

                    return;
                }

                /*
                 * Can happen during a stop or
                 * when replacing an old session.
                 */
                if (code === "aborted") {
                    console.log("[gptVoice] aborted ignored");

                    return;
                }

                state.keepListening = false;
                state.isListening = false;

                clearRestartTimer();

                let message =`Voice recognition error: ${code}`;

                if (code === "not-allowed") {
                    message = "Microphone access was denied.";
                }
                else if (code === "audio-capture") {
                    message = "No microphone was detected.";
                }
                else if (code === "network") {
                    message = "A network error occurred during " +
                        "voice recognition.";
                }

                if (state.dotNetRef) {
                    await state.dotNetRef.invokeMethodAsync("OnVoiceRecognitionError", message);
                }
            };

            recognition.onend = async () => {
                state.isListening = false;

                console.log(
                    "[gptVoice] onend",
                    {
                        currentSessionId,
                        activeSessionId: state.sessionId,
                        keepListening: state.keepListening,
                        manualStopRequested: state.manualStopRequested
                    });

                /*
                 * Ignore events from a previous session.
                 */
                if (
                    currentSessionId !== state.sessionId) {
                    return;
                }

                /*
                 * A natural pause should not send
                 * the prompt.
                 *
                 * We silently restart the same engine.
                 */
                if (
                    state.keepListening && !state.manualStopRequested) {

                    clearRestartTimer();

                    state.restartTimer =
                        window.setTimeout(
                            () => {
                                if (
                                    !state.keepListening ||
                                    state.manualStopRequested ||
                                    currentSessionId !==
                                    state.sessionId) {
                                    return;
                                }

                                try {
                                    console.log("[gptVoice] restarting " + "after natural pause");

                                    recognition.start();
                                }
                                catch (error) {
                                    /*
                                     * Chrome can flag
                                     * InvalidStateError if a start
                                     * is already in progress.
                                     */
                                    if (error?.name !== "InvalidStateError") {
                                        console.warn("[gptVoice] restart failed", error);
                                    }
                                }
                            },
                            350);

                    return;
                }

                state.recognition = null;

                /*
                 * We notify Blazor only after
                 * a manual stop.
                 */
                if (state.manualStopRequested) {
                    await notifyStoppedOnce();
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

        setVoiceOptions(options) {
            state.selectedVoiceName = options?.voiceName || null;
            state.selectedLang = options?.lang || "fr-FR";
            state.rate = Number(options?.rate ?? 0.95);
            state.pitch = Number(options?.pitch ?? 1.0);
            state.volume = Number(options?.volume ?? 1.0);

            return { ok: true };
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
                    utterance.rate = state.rate ?? 0.95;
                    utterance.pitch = state.pitch ?? 1.0;
                    utterance.volume = state.volume ?? 1.0;

                    const voices = window.speechSynthesis.getVoices();

                    const selectedVoiceName = state.selectedVoiceName;
                    const selectedLang = lang || state.selectedLang || "fr-FR";

                    utterance.lang = selectedLang;

                    utterance.voice =
                        voices.find(v => v.name === selectedVoiceName) ||
                        voices.find(v => v.lang === selectedLang) ||
                        voices.find(v => v.lang?.startsWith(selectedLang.split("-")[0])) ||
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

        getVoices() {
            if (!("speechSynthesis" in window)) {
                return [];
            }

            const voices = window.speechSynthesis.getVoices();

            return voices.map(v => ({
                name: v.name,
                lang: v.lang,
                localService: v.localService,
                default: v.default,
                voiceURI: v.voiceURI
            }));
        },

        loadVoices() {
            return new Promise(resolve => {
                if (!("speechSynthesis" in window)) {
                    resolve([]);
                    return;
                }

                let voices = window.speechSynthesis.getVoices();

                if (voices.length > 0) {
                    resolve(window.gptVoice.getVoices());
                    return;
                }

                window.speechSynthesis.onvoiceschanged = () => {
                    resolve(window.gptVoice.getVoices());
                };

                setTimeout(() => {
                    resolve(window.gptVoice.getVoices());
                }, 500);
            });
        },

        saveVoiceOptions(options) {
            localStorage.setItem("outzen.voice.options", JSON.stringify(options || {}));
            this.setVoiceOptions(options);
            return { ok: true };
        },

        loadVoiceOptionsFromStorage() {
            try {
                const raw = localStorage.getItem("outzen.voice.options");
                if (!raw) return null;

                const options = JSON.parse(raw);
                this.setVoiceOptions(options);
                return options;
            } catch {
                return null;
            }
        },

        async stop() {
            await stopInternal(true);
            return { ok: true };
        }
    };
})();










































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/