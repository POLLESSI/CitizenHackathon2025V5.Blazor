/*wwwroot / js / app / randompong.js*/

(() => {
    const canvas = document.getElementById('game');
    const ctx = canvas.getContext('2d');
    const field = { w: canvas.width, h: canvas.height, wall: 14 };
    const paddle = (x) => ({ x, y: field.h / 2 - 50, w: 14, h: 100, vy: 0 });
    const p1 = paddle(36), p2 = paddle(field.w - 50);
    const ball = { x: field.w / 2, y: field.h / 2, r: 14, vx: 250, vy: 180, face: 0, lastHit: 0 };
    const trail = []; const TRAIL_MAX = 16;

    function serve() {
        ball.x = field.w / 2; ball.y = field.h / 2;
        let angle = (Math.random() * Math.PI / 2 - Math.PI / 4);
        let dir = Math.random() < 0.5 ? -1 : 1;
        let speed = 300;
        ball.vx = Math.cos(angle) * speed * dir;
        ball.vy = Math.sin(angle) * speed;
        trail.length = 0;
    }

    // Drawing of the smiley
    function drawSmiley(x, y, r, face) {
        ctx.beginPath();
        ctx.arc(x, y, r, 0, Math.PI * 2);
        ctx.fillStyle = "yellow";
        ctx.fill();
        ctx.strokeStyle = "black";
        ctx.lineWidth = 2;
        ctx.stroke();

        // Eyes
        ctx.fillStyle = "black";
        ctx.beginPath(); ctx.arc(x - r / 3, y - r / 4, r / 6, 0, Math.PI * 2); ctx.fill();
        ctx.beginPath(); ctx.arc(x + r / 3, y - r / 4, r / 6, 0, Math.PI * 2); ctx.fill();

        // Mouth
        ctx.beginPath();
        if (face === 0) { // smile
            ctx.arc(x, y + r / 6, r / 2, 0, Math.PI);
        } else if (face === 1) { // surprised
            ctx.arc(x, y + r / 6, r / 4, 0, Math.PI * 2);
        } else { // sad
            ctx.arc(x, y + r / 1.8, r / 2, Math.PI, 0, true);
        }
        ctx.stroke();
    }

    // Small stars to symbolize shock
    function drawStars(x, y) {
        ctx.fillStyle = "white";
        for (let i = 0; i < 5; i++) {
            let angle = Math.random() * Math.PI * 2;
            let dist = ball.r + 8 + Math.random() * 8;
            let sx = x + Math.cos(angle) * dist;
            let sy = y + Math.sin(angle) * dist;
            ctx.beginPath();
            ctx.arc(sx, sy, 3, 0, Math.PI * 2);
            ctx.fill();
        }
    }

    function update(dt) {
        const aiSpeed = 260;
        if (ball.y < p1.y + p1.h / 2) p1.y -= aiSpeed * dt;
        if (ball.y > p1.y + p1.h / 2) p1.y += aiSpeed * dt;
        if (ball.y < p2.y + p2.h / 2) p2.y -= aiSpeed * dt;
        if (ball.y > p2.y + p2.h / 2) p2.y += aiSpeed * dt;
        p1.y = Math.max(field.wall, Math.min(field.h - field.wall - p1.h, p1.y));
        p2.y = Math.max(field.wall, Math.min(field.h - field.wall - p2.h, p2.y));

        ball.x += ball.vx * dt; ball.y += ball.vy * dt;

        if (ball.y - ball.r <= field.wall) {
            ball.y = field.wall + ball.r; ball.vy *= -1;
            ball.face = 1; ball.lastHit = Date.now();
        }
        if (ball.y + ball.r >= field.h - field.wall) {
            ball.y = field.h - field.wall - ball.r; ball.vy *= -1;
            ball.face = 1; ball.lastHit = Date.now();
        }

        const hit = (p) => ball.x + ball.r > p.x && ball.x - ball.r < p.x + p.w && ball.y + ball.r > p.y && ball.y - ball.r < p.y + p.h;
        if (ball.vx < 0 && hit(p1)) {
            ball.x = p1.x + p1.w + ball.r; ball.vx *= -1.05;
            ball.face = 2; ball.lastHit = Date.now();
        }
        if (ball.vx > 0 && hit(p2)) {
            ball.x = p2.x - ball.r; ball.vx *= -1.05;
            ball.face = 2; ball.lastHit = Date.now();
        }

        if (ball.x < -ball.r || ball.x > field.w + ball.r) serve();

        trail.push({ x: ball.x, y: ball.y }); if (trail.length > TRAIL_MAX) trail.shift();
    }

    function roundRect(x, y, w, h, r) {
        const rr = Math.min(r, Math.min(w, h) / 2);
        ctx.beginPath();
        ctx.moveTo(x + rr, y);
        ctx.arcTo(x + w, y, x + w, y + h, rr);
        ctx.arcTo(x + w, y + h, x, y + h, rr);
        ctx.arcTo(x, y + h, x, y, rr);
        ctx.arcTo(x, y, x + w, y, rr);
        ctx.closePath();
    }

    function draw() {
        ctx.clearRect(0, 0, field.w, field.h);
        ctx.fillStyle = 'rgba(255,255,255,0.2)';
        roundRect(0, 0, field.w, field.wall, 10); ctx.fill();
        roundRect(0, field.h - field.wall, field.w, field.wall, 10); ctx.fill();

        ctx.fillStyle = '#66d2ff';
        roundRect(p1.x, p1.y, p1.w, p1.h, 8); ctx.fill();
        roundRect(p2.x, p2.y, p2.w, p2.h, 8); ctx.fill();

        ctx.save();
        for (let i = 0; i < trail.length; i++) {
            const t = trail[i]; const alpha = (i + 1) / trail.length * 0.6;
            ctx.globalAlpha = alpha;
            ctx.beginPath(); ctx.arc(t.x, t.y, ball.r * (0.8 + i / trail.length * 0.5), 0, Math.PI * 2);
            ctx.fillStyle = 'rgba(128,231,255,0.9)'; ctx.fill();
        }
        ctx.restore();

        // Smiley
        drawSmiley(ball.x, ball.y, ball.r, ball.face);

        // Stars if recent contact
        if (Date.now() - ball.lastHit < 200) {
            drawStars(ball.x, ball.y);
        }
    }

    let last = 0;
    function loop(t) {
        let dt = (t - last) / 1000; if (!last) dt = 0; last = t;
        update(dt); draw();
        requestAnimationFrame(loop);
    }

    serve();
    requestAnimationFrame(loop);
})();







































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/