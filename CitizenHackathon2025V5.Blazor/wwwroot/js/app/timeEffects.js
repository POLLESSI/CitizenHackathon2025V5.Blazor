export function initTimeBasedEffects() {
    const canvas = document.getElementById('bgCanvas');
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    const w = canvas.width = window.innerWidth;
    const h = canvas.height = window.innerHeight;

    const stars = Array.from({ length: 100 }, () => ({
        x: Math.random() * w,
        y: Math.random() * h,
        r: Math.random() * 1.5,
        dx: 0.5 + Math.random()
    }));

    function draw() {
        ctx.clearRect(0, 0, w, h);
        stars.forEach(s => {
            ctx.beginPath();
            ctx.arc(s.x, s.y, s.r, 0, Math.PI * 2);
            ctx.fillStyle = 'white';
            ctx.fill();
            s.x -= s.dx;
            if (s.x < 0) s.x = w;
        });
        requestAnimationFrame(draw);
    }

    draw();
}

window.initScrollFade = function () {
    const sections = document.querySelectorAll(".fade-on-scroll");
    window.addEventListener("scroll", () => {
        sections.forEach(sec => {
            const rect = sec.getBoundingClientRect();
            const opacity = 1 - Math.max(0, rect.top / window.innerHeight);
            sec.style.opacity = Math.max(0, Math.min(opacity, 1));
        });
    });
};



























































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/