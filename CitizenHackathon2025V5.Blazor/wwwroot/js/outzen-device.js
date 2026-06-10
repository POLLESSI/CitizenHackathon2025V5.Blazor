window.outzenDevice = {
    getOrCreateDeviceId: function () {
        const key = "outzen.deviceId";
        let value = localStorage.getItem(key);

        if (!value) {
            value = crypto.randomUUID();
            localStorage.setItem(key, value);
        }

        return value;
    }
};














































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/