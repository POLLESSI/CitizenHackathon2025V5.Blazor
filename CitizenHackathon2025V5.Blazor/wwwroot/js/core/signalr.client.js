window.signalRClient = (function () {

    function showToast(message, title = "SignalR Event", type = "info") {
        // Create the container if it does not exist
        let container = document.getElementById("signalr-toast-container");
        if (!container) {
            container = document.createElement("div");
            container.id = "signalr-toast-container";
            container.style.position = "fixed";
            container.style.top = "20px";
            container.style.right = "20px";
            container.style.zIndex = "9999";
            document.body.appendChild(container);
        }

        // Create the toast
        let toast = document.createElement("div");
        toast.style.minWidth = "250px";
        toast.style.marginBottom = "10px";
        toast.style.padding = "10px 15px";
        toast.style.borderRadius = "8px";
        toast.style.boxShadow = "0 2px 6px rgba(0,0,0,0.2)";
        toast.style.fontFamily = "Arial, sans-serif";
        toast.style.color = "#fff";
        toast.style.cursor = "pointer";
        toast.style.transition = "opacity 0.5s ease";

        // Color depending on type
        switch (type) {
            case "success": toast.style.backgroundColor = "#28a745"; break;
            case "error": toast.style.backgroundColor = "#dc3545"; break;
            case "warning": toast.style.backgroundColor = "#ffc107"; toast.style.color = "#000"; break;
            default: toast.style.backgroundColor = "#007bff"; break;
        }

        // Title + message
        toast.innerHTML = `<strong>${title}</strong><br>${message}`;

        // Close on click
        toast.onclick = function () {
            toast.style.opacity = "0";
            setTimeout(() => toast.remove(), 500);
        };

        container.appendChild(toast);

        // Self-delete after 4 seconds
        setTimeout(() => {
            toast.style.opacity = "0";
            setTimeout(() => toast.remove(), 500);
        }, 4000);
    }

    return {
        showToast: showToast
    };

})();




































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/