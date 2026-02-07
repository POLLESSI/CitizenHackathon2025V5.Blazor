/*wwwroot / js / interop.js*/

function safeLocalStorage() {
    try { return window.localStorage; } catch { return null; }
}

window.jsInterop = {
    setLocalStorage: (k, v) => { const ls = safeLocalStorage(); if (ls) ls.setItem(k, v); },
    getLocalStorage: (k) => { const ls = safeLocalStorage(); return ls ? ls.getItem(k) : null; },
    removeLocalStorage: (k) => { const ls = safeLocalStorage(); if (ls) ls.removeItem(k); }
};

// Smooth scroll by id for Blazor JSInterop
    window.scrollIntoViewById = (id, opts) => {
        const el = document.getElementById(id);
        if (el && el.scrollIntoView) el.scrollIntoView(opts || { behavior: 'smooth' });
    };
































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/