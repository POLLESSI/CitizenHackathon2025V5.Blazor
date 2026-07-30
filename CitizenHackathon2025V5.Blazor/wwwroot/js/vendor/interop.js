/* wwwroot/js/vendor/interop.js */

function safeLocalStorage() {
    try {
        return window.localStorage;
    }
    catch {
        return null;
    }
}

window.jsInterop = {
    setLocalStorage: (key, value) => {
        const storage = safeLocalStorage();
        if (storage) {storage.setItem(key, value);
        }
    },

    getLocalStorage: key => {
        const storage = safeLocalStorage();

        return storage ? storage.getItem(key) : null;
    },

    removeLocalStorage: key => {
        const storage = safeLocalStorage();

        if (storage) {
            storage.removeItem(key);
        }
    }
};






























































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/