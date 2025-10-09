/*wwwroot / js / interop.js*/

window.jsInterop = {
    setLocalStorage: (key, value) => localStorage.setItem(key, value),
    getLocalStorage: (key) => localStorage.getItem(key),
    removeLocalStorage: (key) => localStorage.removeItem(key)
};
// Smooth scroll by id for Blazor JSInterop
    window.scrollIntoViewById = (id, opts) => {
        const el = document.getElementById(id);
        if (el && el.scrollIntoView) el.scrollIntoView(opts || { behavior: 'smooth' });
    };
































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/