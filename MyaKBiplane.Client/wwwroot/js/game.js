(function () {
    "use strict";

    const games = new Map();

    const clouds = [];
    for (let i = 0; i < 22; i += 1) {
        clouds.push({
            x: Math.random() * 1600,
            y: 60 + Math.random() * 780,
            radius: 40 + Math.random() * 90,
            speed: 3 + Math.random() * 9,
            alpha: 0.04 + Math.random() * 0.09
        });
    }

    function pathRoundRect(ctx, x, y, width, height, radius) {
        const r = Math.min(radius, width / 2, height / 2);
        ctx.beginPath();
        ctx.moveTo(x + r, y);
        ctx.lineTo(x + width - r, y);
        ctx.quadraticCurveTo(x + width, y, x + width, y + r);
        ctx.lineTo(x + width, y + height - r);
        ctx.quadraticCurveTo(x + width, y + height, x + width - r, y + height);
        ctx.lineTo(x + r, y + height);
        ctx.quadraticCurveTo(x, y + height, x, y + height - r);
        ctx.lineTo(x, y + r);
        ctx.quadraticCurveTo(x, y, x + r, y);
        ctx.closePath();
    }

    function resize(game) {
        const rect = game.canvas.getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        const cssWidth = Math.max(1, rect.width || window.innerWidth);
        const cssHeight = Math.max(1, rect.height || window.innerHeight);

        game.cssWidth = cssWidth;
        game.cssHeight = cssHeight;
        game.canvas.width = Math.max(1, Math.round(cssWidth * dpr));
        game.canvas.height = Math.max(1, Math.round(cssHeight * dpr));

        game.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        game.ctx.imageSmoothingEnabled = true;

        updateView(game, {
            worldWidth: game.view.worldWidth,
            worldHeight: game.view.worldHeight
        });
    }

    function updateView(game, state) {
        const worldWidth = state.worldWidth || 1600;
        const worldHeight = state.worldHeight || 900;
        const scale = Math.min(game.cssWidth / worldWidth, game.cssHeight / worldHeight);

        game.view = {
            worldWidth,
            worldHeight,
            scale,
            offsetX: (game.cssWidth - worldWidth * scale) / 2,
            offsetY: (game.cssHeight - worldHeight * scale) / 2
        };
    }

    function updatePointer(game, event) {
        const rect = game.canvas.getBoundingClientRect();
        const cssX = event.clientX - rect.left;
        const cssY = event.clientY - rect.top;

        game.mouse.worldX = (cssX - game.view.offsetX) / game.view.scale;
        game.mouse.worldY = (cssY - game.view.offsetY) / game.view.scale;
    }

    function buildInput(game) {
        const keys = game.keys;
        const right = keys.has("KeyD") || keys.has("ArrowRight") ? 1 : 0;
        const left = keys.has("KeyA") || keys.has("ArrowLeft") ? 1 : 0;
        const up = keys.has("KeyW") || keys.has("ArrowUp") ? 1 : 0;
        const down = keys.has("KeyS") || keys.has("ArrowDown") ? 1 : 0;

        const input = {
            thrust: up - down,
            turn: right - left,
            moveX: right - left,
            moveY: down - up,
            aimX: game.mouse.worldX,
            aimY: game.mouse.worldY,
            fire: game.mouse.down || keys.has("KeyF"),
            ability: keys.has("Space"),
            restart: game.restartPressed
        };

        game.restartPressed = false;
        return input;
    }

    async function frame(game, timestamp) {
        if (!game.running) {
            return;
        }

        const input = buildInput(game);
        let stateJson;

        try {
            stateJson = await game.dotNetRef.invokeMethodAsync("Tick", timestamp, input);
        } catch (error) {
            console.error("MyaKBiplane: Tick failed", error);
            game.running = false;
            return;
        }

        if (!game.running) {
            return;
        }

        if (stateJson) {
            try {
                const state = JSON.parse(stateJson);
                updateView(game, state);
                draw(game, state);
            } catch (error) {
                console.error("MyaKBiplane: render state is invalid", error);
            }
        }

        game.rafId = window.requestAnimationFrame((nextTimestamp) => frame(game, nextTimestamp));
    }

    function draw(game, state) {
        const ctx = game.ctx;
        const view = game.view;
        const width = game.cssWidth;
        const height = game.cssHeight;

        const sky = ctx.createLinearGradient(0, 0, 0, height);
        sky.addColorStop(0, "#071a32");
        sky.addColorStop(0.55, "#0b2745");
        sky.addColorStop(1, "#123b5c");
        ctx.fillStyle = sky;
        ctx.fillRect(0, 0, width, height);

        ctx.save();
        ctx.translate(view.offsetX, view.offsetY);
        ctx.scale(view.scale, view.scale);
        ctx.beginPath();
        ctx.rect(0, 0, view.worldWidth, view.worldHeight);
        ctx.clip();

        drawClouds(ctx, state.elapsed || 0, view);
        drawWorldBorder(ctx, view);

        for (const projectile of state.projectiles || []) {
            drawProjectile(ctx, projectile);
        }

        for (const plane of state.planes || []) {
            if (!plane.alive) {
                continue;
            }

            ctx.save();
            ctx.translate(plane.x, plane.y);
            ctx.rotate(plane.angle);

            if (plane.kind === "pterodactyl") {
                drawPterodactyl(ctx, plane, state.elapsed || 0);
            } else {
                drawBiplane(ctx, plane, state.elapsed || 0);
            }

            ctx.restore();
            drawPlaneLabel(ctx, plane);
        }

        for (const particle of state.particles || []) {
            ctx.globalAlpha = Math.max(0, Math.min(1, particle.alpha || 0));
            ctx.fillStyle = particle.color || "#ffffff";
            ctx.beginPath();
            ctx.arc(particle.x, particle.y, Math.max(0.5, particle.size || 1), 0, Math.PI * 2);
            ctx.fill();
        }

        ctx.globalAlpha = 1;
        ctx.restore();

        drawHud(game, state);
    }

    function drawClouds(ctx, elapsed, view) {
        const drift = elapsed * 5;

        for (const cloud of clouds) {
            const x = ((cloud.x + drift * cloud.speed) % (view.worldWidth + 240)) - 120;
            const y = cloud.y;

            ctx.globalAlpha = cloud.alpha;
            ctx.fillStyle = "#dbeafe";
            ctx.beginPath();
            ctx.ellipse(x, y, cloud.radius, cloud.radius * 0.42, 0, 0, Math.PI * 2);
            ctx.ellipse(x + cloud.radius * 0.5, y - cloud.radius * 0.12, cloud.radius * 0.7, cloud.radius * 0.34, 0, 0, Math.PI * 2);
            ctx.ellipse(x - cloud.radius * 0.55, y + cloud.radius * 0.05, cloud.radius * 0.55, cloud.radius * 0.3, 0, 0, Math.PI * 2);
            ctx.fill();
        }

        ctx.globalAlpha = 1;
    }

    function drawWorldBorder(ctx, view) {
        ctx.lineWidth = 4;
        ctx.strokeStyle = "rgba(56, 189, 248, 0.22)";
        ctx.strokeRect(2, 2, view.worldWidth - 4, view.worldHeight - 4);

        ctx.lineWidth = 1;
        ctx.strokeStyle = "rgba(148, 163, 184, 0.08)";
        for (let x = 100; x < view.worldWidth; x += 100) {
            ctx.beginPath();
            ctx.moveTo(x, 0);
            ctx.lineTo(x, view.worldHeight);
            ctx.stroke();
        }

        for (let y = 100; y < view.worldHeight; y += 100) {
            ctx.beginPath();
            ctx.moveTo(0, y);
            ctx.lineTo(view.worldWidth, y);
            ctx.stroke();
        }
    }

    function drawProjectile(ctx, projectile) {
        const length = projectile.length || (projectile.kind === "spear" ? 40 : 16);
        const color = projectile.isPlayer ? "#67e8f9" : "#fca5a5";

        ctx.save();
        ctx.translate(projectile.x, projectile.y);
        ctx.rotate(projectile.angle);
        ctx.lineCap = "round";
        ctx.lineWidth = projectile.kind === "spear" ? 3 : 2;
        ctx.strokeStyle = color;
        ctx.shadowColor = color;
        ctx.shadowBlur = 10;

        ctx.beginPath();
        ctx.moveTo(-length / 2, 0);
        ctx.lineTo(length / 2, 0);
        ctx.stroke();

        if (projectile.kind === "spear") {
            ctx.fillStyle = "#f8fafc";
            ctx.beginPath();
            ctx.moveTo(length / 2 + 7, 0);
            ctx.lineTo(length / 2 - 3, -5);
            ctx.lineTo(length / 2 - 3, 5);
            ctx.closePath();
            ctx.fill();
        }

        ctx.restore();
    }

    function drawBiplane(ctx, plane, elapsed) {
        const color = plane.isPlayer ? "#38bdf8" : "#f87171";
        const fill = plane.hitFlash > 0 ? "#ffffff" : color;

        ctx.lineWidth = 2;
        ctx.strokeStyle = "rgba(2, 6, 23, 0.65)";
        ctx.fillStyle = fill;

        pathRoundRect(ctx, -27, -6, 54, 12, 6);
        ctx.fill();
        ctx.stroke();

        ctx.fillStyle = plane.hitFlash > 0 ? "#ffffff" : color;
        ctx.fillRect(-8, -27, 16, 18);
        ctx.fillRect(-8, 9, 16, 18);
        ctx.strokeRect(-8, -27, 16, 18);
        ctx.strokeRect(-8, 9, 16, 18);

        ctx.fillRect(-27, -13, 7, 26);
        ctx.strokeRect(-27, -13, 7, 26);

        ctx.fillStyle = "rgba(2, 6, 23, 0.7)";
        ctx.beginPath();
        ctx.arc(1, 0, 4.5, 0, Math.PI * 2);
        ctx.fill();

        ctx.save();
        ctx.translate(29, 0);
        ctx.rotate(elapsed * 38);
        ctx.strokeStyle = "rgba(226, 232, 240, 0.75)";
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.moveTo(0, -11);
        ctx.lineTo(0, 11);
        ctx.stroke();
        ctx.restore();

        if (plane.boost > 0) {
            ctx.fillStyle = "rgba(251, 146, 60, 0.85)";
            ctx.beginPath();
            ctx.moveTo(-28, -6);
            ctx.lineTo(-48 - Math.random() * 10, 0);
            ctx.lineTo(-28, 6);
            ctx.closePath();
            ctx.fill();
        }
    }

    function drawPterodactyl(ctx, plane, elapsed) {
        const color = plane.isPlayer ? "#38bdf8" : "#f87171";
        const fill = plane.hitFlash > 0 ? "#ffffff" : color;
        const flap = Math.sin(elapsed * 13) * 7;

        ctx.lineWidth = 2;
        ctx.strokeStyle = "rgba(2, 6, 23, 0.65)";
        ctx.fillStyle = fill;

        ctx.beginPath();
        ctx.moveTo(-2, -3);
        ctx.lineTo(-18, -30 - flap);
        ctx.lineTo(9, -11);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();

        ctx.beginPath();
        ctx.moveTo(-2, 3);
        ctx.lineTo(-18, 30 + flap);
        ctx.lineTo(9, 11);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();

        ctx.beginPath();
        ctx.ellipse(-2, 0, 20, 7, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();

        ctx.beginPath();
        ctx.moveTo(17, -5);
        ctx.lineTo(31, 0);
        ctx.lineTo(17, 5);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();

        ctx.beginPath();
        ctx.moveTo(-22, -6);
        ctx.lineTo(-36, 0);
        ctx.lineTo(-22, 6);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();

        ctx.fillStyle = "rgba(2, 6, 23, 0.7)";
        ctx.beginPath();
        ctx.arc(-1, -3, 4, 0, Math.PI * 2);
        ctx.fill();

        if (plane.charge > 0) {
            ctx.strokeStyle = `rgba(250, 204, 21, ${0.25 + plane.charge * 0.65})`;
            ctx.lineWidth = 2 + plane.charge * 2;
            ctx.beginPath();
            ctx.arc(0, 0, 30 + plane.charge * 8, 0, Math.PI * 2);
            ctx.stroke();

            ctx.beginPath();
            ctx.moveTo(30, 0);
            ctx.lineTo(30 + 45 * plane.charge, 0);
            ctx.stroke();
        }

        if (plane.dashCooldown > 2.25) {
            ctx.fillStyle = "rgba(167, 243, 208, 0.7)";
            ctx.beginPath();
            ctx.moveTo(-24, -5);
            ctx.lineTo(-44, 0);
            ctx.lineTo(-24, 5);
            ctx.closePath();
            ctx.fill();
        }
    }

    function drawPlaneLabel(ctx, plane) {
        const width = 62;
        const healthRatio = Math.max(0, Math.min(1, plane.health / plane.maxHealth));
        const barColor = healthRatio > 0.55 ? "#4ade80" : healthRatio > 0.25 ? "#facc15" : "#f87171";

        ctx.fillStyle = "rgba(2, 6, 23, 0.65)";
        ctx.fillRect(plane.x - width / 2, plane.y - 48, width, 7);
        ctx.fillStyle = barColor;
        ctx.fillRect(plane.x - width / 2 + 1, plane.y - 47, (width - 2) * healthRatio, 5);

        ctx.font = "600 13px 'Segoe UI', sans-serif";
        ctx.textAlign = "center";
        ctx.fillStyle = plane.isPlayer ? "#bae6fd" : "#fecaca";
        ctx.fillText(plane.isPlayer ? "YOU" : "BOT", plane.x, plane.y - 55);
    }

    function drawHud(game, state) {
        const ctx = game.ctx;
        const width = game.cssWidth;
        const player = (state.planes || []).find((plane) => plane.isPlayer);
        const scoreText = `YOU  ${state.playerScore || 0}  :  ${state.botScore || 0}  BOT`;

        ctx.save();
        ctx.font = "700 16px 'Segoe UI', sans-serif";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";

        const scoreWidth = ctx.measureText(scoreText).width + 36;
        ctx.fillStyle = "rgba(2, 6, 23, 0.62)";
        pathRoundRect(ctx, (width - scoreWidth) / 2, 14, scoreWidth, 36, 12);
        ctx.fill();
        ctx.strokeStyle = "rgba(148, 163, 184, 0.2)";
        ctx.lineWidth = 1;
        ctx.stroke();

        ctx.fillStyle = "#e2e8f0";
        ctx.fillText(scoreText, width / 2, 33);

        if (player) {
            drawAbilityHud(ctx, game, player);
        }

        if (state.status === "finished") {
            drawFinishedOverlay(ctx, game, state);
        }

        ctx.restore();
    }

    function drawAbilityHud(ctx, game, player) {
        const x = game.cssWidth - 230;
        const y = game.cssHeight - 92;
        const width = 210;
        const height = 62;

        ctx.fillStyle = "rgba(2, 6, 23, 0.62)";
        pathRoundRect(ctx, x, y, width, height, 12);
        ctx.fill();
        ctx.strokeStyle = "rgba(148, 163, 184, 0.2)";
        ctx.lineWidth = 1;
        ctx.stroke();

        ctx.textAlign = "left";
        ctx.font = "600 12px 'Segoe UI', sans-serif";

        if (player.kind === "pterodactyl") {
            ctx.fillStyle = "#94a3b8";
            ctx.fillText("ЗАРЯД КОПЬЯ", x + 14, y + 19);

            const charge = Math.max(0, Math.min(1, player.charge || 0));
            ctx.fillStyle = "rgba(15, 23, 42, 0.9)";
            ctx.fillRect(x + 14, y + 27, width - 28, 8);
            ctx.fillStyle = "#facc15";
            ctx.fillRect(x + 14, y + 27, (width - 28) * charge, 8);

            ctx.fillStyle = player.reload > 0 ? "#fca5a5" : "#94a3b8";
            ctx.fillText(
                player.reload > 0 ? `Перезарядка: ${player.reload.toFixed(1)} с` : "Готов к броску",
                x + 14,
                y + 51);
        } else {
            ctx.fillStyle = "#94a3b8";
            ctx.fillText("УСКОРЕНИЕ", x + 14, y + 19);

            const ready = player.boostCooldown <= 0;
            const active = player.boost > 0;
            ctx.fillStyle = active ? "#22d3ee" : ready ? "#4ade80" : "#475569";
            pathRoundRect(ctx, x + 14, y + 26, width - 28, 10, 5);
            ctx.fill();

            ctx.fillStyle = "#cbd5e1";
            ctx.fillText(
                active ? `Активно: ${player.boost.toFixed(1)} с` : ready ? "Готово" : `Кулдаун: ${player.boostCooldown.toFixed(1)} с`,
                x + 14,
                y + 51);
        }
    }

    function drawFinishedOverlay(ctx, game, state) {
        const width = game.cssWidth;
        const height = game.cssHeight;

        ctx.fillStyle = "rgba(2, 6, 23, 0.66)";
        ctx.fillRect(0, 0, width, height);

        const title = state.winner === "player"
            ? "ПОБЕДА"
            : state.winner === "bot"
                ? "ПОРАЖЕНИЕ"
                : "НИЧЬЯ";

        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillStyle = state.winner === "player" ? "#7dd3fc" : state.winner === "bot" ? "#fca5a5" : "#e2e8f0";
        ctx.font = "800 64px 'Segoe UI', sans-serif";
        ctx.fillText(title, width / 2, height / 2 - 30);

        ctx.fillStyle = "#cbd5e1";
        ctx.font = "500 18px 'Segoe UI', sans-serif";
        ctx.fillText("R — реванш    Esc — в меню", width / 2, height / 2 + 42);
    }

    function attachInput(game) {
        const canvas = game.canvas;

        const onKeyDown = (event) => {
            if (["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Space"].includes(event.code)) {
                event.preventDefault();
            }

            if (event.code === "Escape" && !event.repeat) {
                game.dotNetRef.invokeMethodAsync("ExitToMenu");
                return;
            }

            if (event.code === "KeyR" && !event.repeat) {
                game.restartPressed = true;
            }

            game.keys.add(event.code);
        };

        const onKeyUp = (event) => {
            game.keys.delete(event.code);
        };

        const onBlur = () => {
            game.keys.clear();
            game.mouse.down = false;
        };

        const onPointerMove = (event) => {
            updatePointer(game, event);
        };

        const onPointerDown = (event) => {
            if (event.button !== 0) {
                return;
            }

            event.preventDefault();
            updatePointer(game, event);
            game.mouse.down = true;
            canvas.setPointerCapture?.(event.pointerId);
        };

        const onPointerUp = (event) => {
            if (event.button !== 0) {
                return;
            }

            game.mouse.down = false;
            canvas.releasePointerCapture?.(event.pointerId);
        };

        const onPointerCancel = () => {
            game.mouse.down = false;
        };

        const onContextMenu = (event) => {
            event.preventDefault();
        };

        const onResize = () => {
            resize(game);
        };

        window.addEventListener("keydown", onKeyDown);
        window.addEventListener("keyup", onKeyUp);
        window.addEventListener("blur", onBlur);
        window.addEventListener("resize", onResize);
        canvas.addEventListener("pointermove", onPointerMove);
        canvas.addEventListener("pointerdown", onPointerDown);
        canvas.addEventListener("pointerup", onPointerUp);
        canvas.addEventListener("pointercancel", onPointerCancel);
        canvas.addEventListener("contextmenu", onContextMenu);

        game.handlers = {
            onKeyDown,
            onKeyUp,
            onBlur,
            onPointerMove,
            onPointerDown,
            onPointerUp,
            onPointerCancel,
            onContextMenu,
            onResize
        };
    }

    function detachInput(game) {
        const canvas = game.canvas;
        const handler = game.handlers;

        window.removeEventListener("keydown", handler.onKeyDown);
        window.removeEventListener("keyup", handler.onKeyUp);
        window.removeEventListener("blur", handler.onBlur);
        window.removeEventListener("resize", handler.onResize);
        canvas.removeEventListener("pointermove", handler.onPointerMove);
        canvas.removeEventListener("pointerdown", handler.onPointerDown);
        canvas.removeEventListener("pointerup", handler.onPointerUp);
        canvas.removeEventListener("pointercancel", handler.onPointerCancel);
        canvas.removeEventListener("contextmenu", handler.onContextMenu);
    }

    function start(dotNetRef, canvasId, playerKind) {
        stop(canvasId);

        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            console.error(`MyaKBiplane: canvas '${canvasId}' not found`);
            return;
        }

        const game = {
            dotNetRef,
            canvas,
            ctx: canvas.getContext("2d"),
            playerKind,
            keys: new Set(),
            mouse: { worldX: 800, worldY: 450, down: false },
            view: { worldWidth: 1600, worldHeight: 900, scale: 1, offsetX: 0, offsetY: 0 },
            cssWidth: 1,
            cssHeight: 1,
            running: true,
            restartPressed: false,
            rafId: 0,
            handlers: {}
        };

        games.set(canvasId, game);
        resize(game);
        attachInput(game);
        game.rafId = window.requestAnimationFrame((timestamp) => frame(game, timestamp));
    }

    function stop(canvasId) {
        const game = games.get(canvasId);
        if (!game) {
            return;
        }

        game.running = false;
        window.cancelAnimationFrame(game.rafId);
        detachInput(game);
        games.delete(canvasId);
    }

    window.myakBiplane = {
        start,
        stop
    };
})();
