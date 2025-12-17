// wwwroot/js/app/outzenAudio.module.js
"use strict";

const AudioCtx = window.AudioContext || window.webkitAudioContext;

let ctx = null;            // created lazily
let unlocked = false;

function ensureCtx() {
    if (!AudioCtx) return null;
    if (!ctx) ctx = new AudioCtx();
    return ctx;
}

async function unlockAudio() {
    const c = ensureCtx();
    if (!c) return false;
    try {
        await c.resume();
        unlocked = (c.state === "running");
    } catch {
        unlocked = false;
    }
    return unlocked;
}

// global one-time unlock hook
(function installUnlockHook() {
    const onGesture = async () => {
        await unlockAudio();
        window.removeEventListener("pointerdown", onGesture);
        window.removeEventListener("keydown", onGesture);
    };
    window.addEventListener("pointerdown", onGesture, { once: true });
    window.addEventListener("keydown", onGesture, { once: true });
})();

const KEY = "ozBeepCfg";
const cfg = (() => {
    const def = { volume: 0.06, freq: 880, minIntervalMs: 1500, onlyWhenVisible: true, muted: false };
    try {
        const raw = localStorage.getItem(KEY);
        return raw ? { ...def, ...JSON.parse(raw) } : def;
    } catch { return def; }
})();

function save() { try { localStorage.setItem(KEY, JSON.stringify(cfg)); } catch { } }

const last = new Map();

function canPlayNow(onlyWhenVisible) {
    if (cfg.muted) return false;
    if (onlyWhenVisible && document.visibilityState !== "visible") return false;
    return true;
}

function beepOnce(durationMs = 120, freq = cfg.freq, volume = cfg.volume) {
    const c = ensureCtx();
    if (!c) return false;
    if (!unlocked || c.state !== "running") return false;

    const o = c.createOscillator();
    const g = c.createGain();
    o.type = "sine";
    o.frequency.value = freq;
    g.gain.value = volume;
    o.connect(g);
    g.connect(c.destination);

    o.start();
    const endAt = c.currentTime + durationMs / 1000;
    try { g.gain.exponentialRampToValueAtTime(0.0001, endAt - 0.04); } catch { }
    o.stop(endAt);
    return true;
}

// Exposed for Blazor if you want to force unlocking (e.g., "Enable sound" button)
export async function enableSound() {
    return await unlockAudio();
}

export async function beepCritical(id, overrides) {
    const vol = Math.min(0.08, Math.max(0.03, Number(overrides?.volume ?? cfg.volume)));
    const fq = Math.min(1200, Math.max(700, Number(overrides?.freq ?? cfg.freq)));
    const onlyWhenVisible = Boolean(overrides?.onlyWhenVisible ?? cfg.onlyWhenVisible);

    if (!canPlayNow(onlyWhenVisible)) return false;

    const now = Date.now();
    const prev = last.get(id) || 0;
    if (now - prev < (cfg.minIntervalMs || 1500)) return false;
    last.set(id, now);

    // Try unlock if still locked (won't work without a gesture, but won't spam console)
    if (!unlocked) await unlockAudio();

    return beepOnce(120, fq, vol);
}

export function mute() { cfg.muted = true; save(); }
export function unmute() { cfg.muted = false; save(); }

export async function testBeep(overrides) {
    return await beepCritical("__test__", overrides);
}

export function setBeepConfig(next = {}) {
    if (typeof next.volume === "number") cfg.volume = Math.min(0.08, Math.max(0.03, next.volume));
    if (typeof next.freq === "number") cfg.freq = Math.min(1200, Math.max(700, next.freq));
    if (typeof next.minIntervalMs === "number") cfg.minIntervalMs = Math.max(200, next.minIntervalMs | 0);
    if (typeof next.onlyWhenVisible === "boolean") cfg.onlyWhenVisible = next.onlyWhenVisible;
    if (typeof next.muted === "boolean") cfg.muted = next.muted;
    save();
    return { ...cfg };
}

export function getBeepConfig() { return { ...cfg }; }

