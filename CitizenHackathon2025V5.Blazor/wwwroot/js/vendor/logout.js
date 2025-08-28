window.outzenLogout = {
    autoLogout: async function (refreshToken) {
        try {
            await fetch("/api/auth/logout", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(refreshToken),
                credentials: "include"
            });
        } catch (err) {
            console.warn("Logout auto failed:", err);
        }
    }
};

// Triggered when closing/refreshing the tab
window.addEventListener("beforeunload", function () {
    if (window.localStorage.getItem("refreshToken")) {
        window.outzenLogout.autoLogout(window.localStorage.getItem("refreshToken"));
    }
});































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/