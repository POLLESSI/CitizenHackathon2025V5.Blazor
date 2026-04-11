/*wwwroot/js/crowdbar.js*/
window.OutZenCrowd = {
    updateBar: function (id, percent) {
        const el = document.getElementById(`crowdbar-${id}`);
        if (!el) return;

        // maj la largeur
        const fill = el.querySelector('.crowdbar__fill');
        const label = el.querySelector('.crowdbar__label');
        if (fill) fill.style.width = `${percent}%`;
        if (label) label.textContent = `${percent}%`;

        // bump anim
        el.classList.remove('bumpsoon'); // reset éventuel
        void el.offsetWidth;             // reflow pour relancer l’anim
        el.classList.add('bump');

        // retire la classe après l’anim
        setTimeout(() => el.classList.remove('bump'), 500);
    }
};






































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/