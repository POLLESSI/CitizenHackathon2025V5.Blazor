﻿/*wwwroot / js / app / wasmUtils.js*/

export async function checkWasmAvailable(path = "_framework/blazor.webassembly.js") {
    try {
        const response = await fetch(path, { method: "HEAD" });
        return response.ok;
    } catch (err) {
        return false;
    }
}

export async function checkRuntimeDiagnostic(dotNetRef, resourceUrl) {
    try {
        const response = await fetch(resourceUrl, { method: 'HEAD' });
        await dotNetRef.invokeMethodAsync('ReceiveDiagnosticResult', response.ok);
    } catch (error) {
        await dotNetRef.invokeMethodAsync('ReceiveDiagnosticResult', false);
    }
}
//+++++++++++++++++theme-manager++++++++++++++++++
/*++++++++++++++++++++++++++++++++++++++++++++++++*/
const THEME_KEY = 'user-theme';

const availableThemes = ['dark', 'theme-ultraluxe'];

function applyTheme(themeName) {
    const body = document.body;
    availableThemes.forEach(theme => body.classList.remove(theme));
    if (availableThemes.includes(themeName)) {
        body.classList.add(themeName);
        localStorage.setItem(THEME_KEY, themeName);
        console.log(`Theme enabled : ${themeName}`);
    } else {
        console.warn(`Unknown theme : ${themeName}`);
    }
}

function getCurrentTheme() {
    return localStorage.getItem(THEME_KEY) || 'dark';
}

function init() {
    const savedTheme = getCurrentTheme();
    applyTheme(savedTheme);
}

export const ThemeManager = {
    applyTheme,
    getCurrentTheme,
    init
};

window.setTheme = (themeName) => {
    document.body.className = themeName;
};
/*+++++++++++++++++++++++scrollInterop.js++++++++++++++++++++++*/
/*+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++*/
window.scrollInterop = {
    // 📦 Blazor-compatible functions via ElementReference
    getScrollTop: function (element) {
        return element?.scrollTop || 0;
    },
    getScrollHeight: function (element) {
        return element?.scrollHeight || 0;
    },
    getClientHeight: function (element) {
        return element?.clientHeight || 0;
    },

    // 🧰 Alternative functions by ID (if other components still use them)
    getScrollTopById: function (elementId) {
        const el = document.getElementById(elementId);
        return el ? el.scrollTop : 0;
    },
    getScrollHeightById: function (elementId) {
        const el = document.getElementById(elementId);
        return el ? el.scrollHeight : 0;
    },
    getClientHeightById: function (elementId) {
        const el = document.getElementById(elementId);
        return el ? el.clientHeight : 0;
    }
};
window.init = () => {
    console.log("init() called – alias for compatibility");
    ThemeManager.init(); 
};

window.getScrollTop = () => window.scrollY || document.documentElement.scrollTop || 0;

































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/