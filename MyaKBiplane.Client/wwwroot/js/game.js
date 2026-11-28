(function () {
    "use strict";

    const games = new Map();
    const pilotRadius = 9.5;

    // На тач-устройствах показываем экранные стики и кнопки.
    if (window.matchMedia?.("(pointer: coarse)").matches) {
        document.documentElement.classList.add("has-touch");
    }

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    // Дневная палитра в духе пиксельного концепта: насыщенное небо,
    // белые облака, зелёная трава с коричневой землёй и сине-красная башня.
    const palette = {
        skyTop: "#0a9ad8",
        skyMiddle: "#16b9ec",
        skyBottom: "#4bd6f6",
        cloud: "#f0feff",
        treeLine: "rgba(52, 112, 76, 0.5)",
        grass: "#57c62c",
        grassLight: "#8ae650",
        grassDark: "#2f8c1c",
        dirtTop: "#a87446",
        dirtBottom: "#5f3d22",
        dirtSpeck: "rgba(62, 38, 20, 0.35)",
        towerLeft: "#1b7fc4",
        towerLeftLight: "#3fa0dd",
        towerRight: "#d9302f",
        towerRightLight: "#ef5b56",
        towerBase: "#eaf3f7",
        beacon: "#f2fdff",
        slot0: "#1b7fc4",
        slot1: "#d9302f",
        ink: "rgba(4, 18, 31, 0.68)"
    };

    const clouds = [];
    for (let i = 0; i < 14; i += 1) {
        clouds.push({
            x: Math.random() * 1600,
            y: 60 + Math.random() * 780,
            radius: 40 + Math.random() * 90,
            speed: 3 + Math.random() * 9,
            alpha: 0.22 + Math.random() * 0.18
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
        const coarse = document.documentElement.classList.contains("has-touch")
            || document.documentElement.classList.contains("telegram-mobile");
        const scale = Math.min(game.cssWidth / worldWidth, game.cssHeight / worldHeight)
            * (coarse ? 1.18 : 1);

        let offsetX = (game.cssWidth - worldWidth * scale) / 2;
        let offsetY = (game.cssHeight - worldHeight * scale) / 2;

        if (coarse) {
            // На узком ландшафте держим землю в кадре и приближаем лётчика.
            const groundY = state.groundY ?? game.state?.groundY ?? (worldHeight - 40);
            offsetY = game.cssHeight - 26 - groundY * scale;

            if (worldWidth * scale > game.cssWidth) {
                const own = game.state && findOwnEntity(game.state);
                const focusX = own ? own.x : worldWidth / 2;
                offsetX = clamp(
                    game.cssWidth / 2 - focusX * scale,
                    game.cssWidth - worldWidth * scale,
                    0);
            }
        }

        game.view = {
            worldWidth,
            worldHeight,
            scale,
            offsetX,
            offsetY
        };
    }

    function updatePointer(game, event) {
        const rect = game.canvas.getBoundingClientRect();
        const cssX = event.clientX - rect.left;
        const cssY = event.clientY - rect.top;

        game.mouse.worldX = (cssX - game.view.offsetX) / game.view.scale;
        game.mouse.worldY = (cssY - game.view.offsetY) / game.view.scale;
    }

    function findOwnEntity(state) {
        const planes = state.planes || [];
        const pilots = state.pilots || [];

        return planes.find((plane) => plane.slot === state.yourSlot && plane.alive)
            || planes.find((plane) => plane.slot === state.yourSlot)
            || pilots.find((pilot) => pilot.slot === state.yourSlot)
            || null;
    }

    function resolveAim(game) {
        const state = game.state;
        const touch = game.touch;

        // Правый стик задаёт направление прицела относительно своего аппарата.
        if (touch.aimActive && state) {
            const origin = findOwnEntity(state);

            if (origin) {
                const length = Math.hypot(touch.aimX, touch.aimY) || 1;

                return {
                    x: origin.x + (touch.aimX / length) * 700,
                    y: origin.y + (touch.aimY / length) * 700
                };
            }
        }

        if (game.mouse.aimed) {
            return { x: game.mouse.worldX, y: game.mouse.worldY };
        }

        // Без мыши и стика целимся по носу аппарата: так птеродактиль
        // не бросает копьё в случайную точку экрана.
        const ownPlane = state && (state.planes || []).find(
            (plane) => plane.slot === state.yourSlot && plane.alive);

        if (ownPlane) {
            const angle = ownPlane.angle || 0;

            return {
                x: ownPlane.x + Math.cos(angle) * 700,
                y: ownPlane.y + Math.sin(angle) * 700
            };
        }

        return { x: game.mouse.worldX, y: game.mouse.worldY };
    }

    function buildInput(game) {
        const keys = game.keys;
        const touch = game.touch;
        const right = keys.has("KeyD") || keys.has("ArrowRight") ? 1 : 0;
        const left = keys.has("KeyA") || keys.has("ArrowLeft") ? 1 : 0;
        const up = keys.has("KeyW") || keys.has("ArrowUp") ? 1 : 0;
        const down = keys.has("KeyS") || keys.has("ArrowDown") ? 1 : 0;
        const aim = resolveAim(game);

        const input = {
            thrust: clamp((up - down) + touch.thrust - touch.moveY, -1, 1),
            turn: clamp((right - left) + touch.moveX, -1, 1),
            moveX: clamp((right - left) + touch.moveX, -1, 1),
            moveY: clamp((down - up) + touch.moveY, -1, 1),
            aimX: aim.x,
            aimY: aim.y,
            fire: game.mouse.down || keys.has("KeyF") || touch.fire,
            ability: keys.has("Space") || game.abilityPressed,
            reload: game.reloadPressed,
            eject: game.ejectPressed,
            parachute: game.parachutePressed
        };

        game.reloadPressed = false;
        game.ejectPressed = false;
        game.parachutePressed = false;
        game.abilityPressed = false;
        return input;
    }

    async function frame(game, timestamp) {
        if (!game.running) {
            return;
        }

        const dt = game.lastTimestamp
            ? Math.max(0, Math.min(0.1, (timestamp - game.lastTimestamp) / 1000))
            : 0;
        game.lastTimestamp = timestamp;

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

        updateParticles(game, dt);

        if (stateJson) {
            try {
                const state = JSON.parse(stateJson);

                if (state && state.worldWidth) {
                    game.state = state;
                    updateTouchUi(game, state);
                    updateView(game, state);
                    handleEvents(game, state);
                    draw(game, state);
                }
            } catch (error) {
                console.error("MyaKBiplane: render state is invalid", error);
            }
        }

        game.rafId = window.requestAnimationFrame((nextTimestamp) => frame(game, nextTimestamp));
    }

    function slotColor(slot, yourSlot) {
        return slot === yourSlot ? palette.slot0 : palette.slot1;
    }

    function draw(game, state) {
        const ctx = game.ctx;
        const view = game.view;
        const width = game.cssWidth;
        const height = game.cssHeight;

        const sky = ctx.createLinearGradient(0, 0, 0, height);
        sky.addColorStop(0, palette.skyTop);
        sky.addColorStop(0.55, palette.skyMiddle);
        sky.addColorStop(1, palette.skyBottom);
        ctx.fillStyle = sky;
        ctx.fillRect(0, 0, width, height);

        ctx.save();
        ctx.translate(view.offsetX, view.offsetY);
        ctx.scale(view.scale, view.scale);
        ctx.beginPath();
        ctx.rect(0, 0, view.worldWidth, view.worldHeight);
        ctx.clip();

        drawClouds(ctx, state.elapsed || 0, view);
        drawGround(ctx, state, view);
        drawWorldBorder(ctx, view);
        drawTower(ctx, state, view);

        for (const projectile of state.projectiles || []) {
            drawProjectile(ctx, projectile, state.yourSlot || 0);
        }

        const planes = (state.planes || []).slice().sort((a, b) => {
            if (a.abandoned === b.abandoned) {
                return 0;
            }

            return a.abandoned ? -1 : 1;
        });

        for (const plane of planes) {
            if (!plane.alive) {
                continue;
            }

            ctx.save();

            if (plane.abandoned) {
                ctx.globalAlpha = 0.55;
            }

            ctx.translate(plane.x, plane.y);
            ctx.rotate(plane.angle);

            if (plane.kind === "pterodactyl") {
                drawPterodactyl(ctx, plane, state.elapsed || 0, state.yourSlot || 0);
            } else {
                drawBiplane(ctx, plane, state.elapsed || 0, state.yourSlot || 0);
            }

            ctx.restore();
            drawPlaneLabel(ctx, plane, state);
        }

        for (const pilot of state.pilots || []) {
            drawPilot(ctx, game, pilot, state);
        }

        drawParticles(ctx, game.particles);

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
            ctx.fillStyle = palette.cloud;
            ctx.beginPath();
            ctx.ellipse(x, y, cloud.radius, cloud.radius * 0.42, 0, 0, Math.PI * 2);
            ctx.ellipse(x + cloud.radius * 0.5, y - cloud.radius * 0.12, cloud.radius * 0.7, cloud.radius * 0.34, 0, 0, Math.PI * 2);
            ctx.ellipse(x - cloud.radius * 0.55, y + cloud.radius * 0.05, cloud.radius * 0.55, cloud.radius * 0.3, 0, 0, Math.PI * 2);
            ctx.fill();
        }

        ctx.globalAlpha = 1;
    }

    function drawGround(ctx, state, view) {
        const groundY = state.groundY || (view.worldHeight - 24);
        const groundHeight = Math.max(12, view.worldHeight - groundY);
        const grassHeight = Math.max(6, Math.min(12, groundHeight * 0.4));

        // Лес на горизонте — тёмно-зелёный силуэт, как в концепте.
        ctx.fillStyle = palette.treeLine;
        for (let x = -12; x < view.worldWidth + 24; x += 22) {
            const step = Math.floor((x + 12) / 22);
            const height = 24 + ((step * 7) % 18);

            ctx.beginPath();
            ctx.moveTo(x, groundY);
            ctx.lineTo(x + 11, groundY - height);
            ctx.lineTo(x + 22, groundY);
            ctx.closePath();
            ctx.fill();
        }

        const dirt = ctx.createLinearGradient(0, groundY, 0, view.worldHeight);
        dirt.addColorStop(0, palette.dirtTop);
        dirt.addColorStop(1, palette.dirtBottom);
        ctx.fillStyle = dirt;
        ctx.fillRect(0, groundY, view.worldWidth, groundHeight);

        ctx.fillStyle = palette.grass;
        ctx.fillRect(0, groundY, view.worldWidth, grassHeight);
        ctx.fillStyle = palette.grassLight;
        ctx.fillRect(0, groundY, view.worldWidth, 3);
        ctx.fillStyle = palette.grassDark;
        ctx.fillRect(0, groundY + grassHeight - 2, view.worldWidth, 2);

        // Травинки над кромкой.
        ctx.fillStyle = palette.grassLight;
        for (let x = 4; x < view.worldWidth; x += 18) {
            const height = 2 + (Math.floor(x / 18) % 3);
            ctx.fillRect(x, groundY - height, 2, height);
        }

        // Камешки в почве.
        ctx.fillStyle = palette.dirtSpeck;
        const speckZone = Math.max(1, groundHeight - grassHeight - 8);
        for (let x = 10; x < view.worldWidth; x += 34) {
            const y = groundY + grassHeight + 4 + (Math.floor(x / 34) * 13 % speckZone);
            ctx.fillRect(x, y, 3, 3);
        }
    }

    function drawWorldBorder(ctx, view) {
        ctx.lineWidth = 4;
        ctx.strokeStyle = "rgba(255, 255, 255, 0.16)";
        ctx.strokeRect(2, 2, view.worldWidth - 4, view.worldHeight - 4);

        ctx.lineWidth = 1;
        ctx.strokeStyle = "rgba(255, 255, 255, 0.05)";
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

    function drawTower(ctx, state, view) {
        const tower = state.tower;

        if (!tower) {
            return;
        }

        const cx = tower.x;
        const base = tower.baseY || state.groundY || (view.worldHeight - 24);
        const height = tower.height || 250;
        const width = tower.width || 48;
        const top = base - height;
        const shoulder = width * 0.32;
        const pulse = 0.55 + (0.45 * Math.sin((state.elapsed || 0) * 2.6));

        ctx.save();

        ctx.globalAlpha = 0.12;
        ctx.fillStyle = palette.beacon;
        ctx.beginPath();
        ctx.moveTo(cx - 8, top - 10);
        ctx.lineTo(cx + 8, top - 10);
        ctx.lineTo(cx + 42, base);
        ctx.lineTo(cx - 42, base);
        ctx.closePath();
        ctx.fill();
        ctx.globalAlpha = 1;

        // Левая половина — цвет первого слота.
        ctx.fillStyle = palette.towerLeft;
        ctx.beginPath();
        ctx.moveTo(cx - width / 2, base);
        ctx.lineTo(cx - shoulder, top);
        ctx.lineTo(cx, top);
        ctx.lineTo(cx, base);
        ctx.closePath();
        ctx.fill();

        // Правая половина — цвет второго слота.
        ctx.fillStyle = palette.towerRight;
        ctx.beginPath();
        ctx.moveTo(cx + width / 2, base);
        ctx.lineTo(cx + shoulder, top);
        ctx.lineTo(cx, top);
        ctx.lineTo(cx, base);
        ctx.closePath();
        ctx.fill();

        // Светлые блики по краям, как на башне концепта.
        ctx.fillStyle = palette.towerLeftLight;
        ctx.beginPath();
        ctx.moveTo(cx - width / 2 + 3, base);
        ctx.lineTo(cx - shoulder + 3, top + 6);
        ctx.lineTo(cx - shoulder + 8, top + 6);
        ctx.lineTo(cx - width / 2 + 8, base);
        ctx.closePath();
        ctx.fill();

        ctx.fillStyle = palette.towerRightLight;
        ctx.beginPath();
        ctx.moveTo(cx + width / 2 - 3, base);
        ctx.lineTo(cx + shoulder - 3, top + 6);
        ctx.lineTo(cx + shoulder - 8, top + 6);
        ctx.lineTo(cx + width / 2 - 8, base);
        ctx.closePath();
        ctx.fill();

        ctx.strokeStyle = palette.ink;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(cx, top);
        ctx.lineTo(cx, base);
        ctx.stroke();

        ctx.strokeStyle = "rgba(255, 255, 255, 0.32)";
        ctx.lineWidth = 1;

        for (let y = top + 26; y < base - 10; y += 34) {
            ctx.beginPath();
            ctx.moveTo(cx - width / 2 + 4, y);
            ctx.lineTo(cx + width / 2 - 4, y);
            ctx.stroke();
        }

        // Белый цоколь башни.
        ctx.fillStyle = palette.towerBase;
        ctx.fillRect(cx - width / 2 - 7, base - 12, width + 14, 12);
        ctx.strokeStyle = palette.ink;
        ctx.lineWidth = 2;
        ctx.strokeRect(cx - width / 2 - 7, base - 12, width + 14, 12);

        ctx.shadowColor = "rgba(255, 255, 255, 0.85)";
        ctx.shadowBlur = 24 * pulse;
        ctx.fillStyle = `rgba(236, 252, 255, ${0.7 + pulse * 0.3})`;
        ctx.beginPath();
        ctx.arc(cx, top - 8, 10 + pulse * 2, 0, Math.PI * 2);
        ctx.fill();
        ctx.shadowBlur = 0;

        ctx.fillStyle = "#ffffff";
        ctx.beginPath();
        ctx.arc(cx, top - 8, 4, 0, Math.PI * 2);
        ctx.fill();

        // Сетка на сфере маяка.
        ctx.fillStyle = "rgba(24, 64, 92, 0.5)";
        for (let dy = -4; dy <= 4; dy += 4) {
            for (let dx = -4; dx <= 4; dx += 4) {
                if (dx * dx + dy * dy > 16) {
                    continue;
                }

                ctx.beginPath();
                ctx.arc(cx + dx, top - 8 + dy, 0.9, 0, Math.PI * 2);
                ctx.fill();
            }
        }

        ctx.font = "700 11px 'Segoe UI', sans-serif";
        ctx.textAlign = "center";
        ctx.lineWidth = 3;
        ctx.strokeStyle = "rgba(6, 26, 40, 0.65)";
        ctx.strokeText("МАЯК", cx, top - 28);
        ctx.fillStyle = "#f4fcff";
        ctx.fillText("МАЯК", cx, top - 28);

        // Счёт матча — прямо на башне, а не отдельной плашкой в углу экрана.
        drawTowerScore(ctx, state, cx, top + height * 0.34);

        ctx.restore();
    }

    function drawTowerScore(ctx, state, cx, y) {
        const scores = state.scores || [0, 0];
        const yourSlot = state.yourSlot || 0;
        const yourScore = scores[yourSlot] || 0;
        const opponentScore = scores[1 - yourSlot] || 0;
        const opponentIsBot = (state.planes || []).some((plane) => plane.slot !== yourSlot && plane.isBot);
        const caption = opponentIsBot ? "ВЫ · БОТ" : "ВЫ · СОПЕРНИК";

        ctx.save();
        ctx.textAlign = "left";
        ctx.textBaseline = "middle";

        ctx.font = "700 9px 'Segoe UI', sans-serif";
        const captionWidth = ctx.measureText(caption).width;

        ctx.font = "700 22px 'Segoe UI', sans-serif";
        const yourText = String(yourScore);
        const separator = " : ";
        const opponentText = String(opponentScore);
        const yourWidth = ctx.measureText(yourText).width;
        const separatorWidth = ctx.measureText(separator).width;
        const opponentWidth = ctx.measureText(opponentText).width;
        const scoreWidth = yourWidth + separatorWidth + opponentWidth;

        const plaqueWidth = Math.max(64, scoreWidth + 22, captionWidth + 14);
        const plaqueHeight = 46;
        const left = cx - plaqueWidth / 2;
        const top = y - plaqueHeight / 2;

        ctx.fillStyle = "rgba(6, 22, 36, 0.78)";
        pathRoundRect(ctx, left, top, plaqueWidth, plaqueHeight, 8);
        ctx.fill();
        ctx.strokeStyle = "rgba(255, 255, 255, 0.35)";
        ctx.lineWidth = 1;
        ctx.stroke();

        ctx.textAlign = "center";
        ctx.font = "700 9px 'Segoe UI', sans-serif";
        ctx.fillStyle = "rgba(214, 240, 250, 0.85)";
        ctx.fillText(caption, cx, top + 11);

        ctx.textAlign = "left";
        ctx.font = "700 22px 'Segoe UI', sans-serif";
        let x = cx - scoreWidth / 2;

        ctx.fillStyle = "#8fe0ff";
        ctx.fillText(yourText, x, top + 32);
        x += yourWidth;

        ctx.fillStyle = "rgba(214, 240, 250, 0.7)";
        ctx.fillText(separator, x, top + 32);
        x += separatorWidth;

        ctx.fillStyle = "#ffb3ad";
        ctx.fillText(opponentText, x, top + 32);

        ctx.restore();
    }

    function drawProjectile(ctx, projectile, yourSlot) {
        const color = slotColor(projectile.slot, yourSlot);

        ctx.save();
        ctx.translate(projectile.x, projectile.y);
        ctx.rotate(projectile.angle);
        ctx.shadowColor = color;
        ctx.shadowBlur = 10;

        if (projectile.kind === "spear") {
            const length = projectile.length || 40;
            ctx.lineCap = "round";
            ctx.lineWidth = 3;
            ctx.strokeStyle = color;
            ctx.beginPath();
            ctx.moveTo(-length / 2, 0);
            ctx.lineTo(length / 2, 0);
            ctx.stroke();

            ctx.fillStyle = "#f8fafc";
            ctx.beginPath();
            ctx.moveTo(length / 2 + 7, 0);
            ctx.lineTo(length / 2 - 3, -5);
            ctx.lineTo(length / 2 - 3, 5);
            ctx.closePath();
            ctx.fill();
        } else {
            const radius = Math.max(1.5, projectile.radius || 4.5);
            ctx.fillStyle = color;
            ctx.beginPath();
            ctx.arc(0, 0, radius, 0, Math.PI * 2);
            ctx.fill();
        }

        ctx.restore();
    }

    function drawBiplane(ctx, plane, elapsed, yourSlot) {
        const color = slotColor(plane.slot, yourSlot);
        const fill = plane.hitFlash > 0 ? "#ffffff" : color;

        ctx.lineWidth = 2;
        ctx.strokeStyle = "rgba(2, 6, 23, 0.65)";
        ctx.fillStyle = fill;

        pathRoundRect(ctx, -27, -6, 54, 12, 6);
        ctx.fill();
        ctx.stroke();

        ctx.fillStyle = fill;
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

    function drawPterodactyl(ctx, plane, elapsed, yourSlot) {
        const color = slotColor(plane.slot, yourSlot);
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

    function drawPlaneLabel(ctx, plane, state) {
        const yourSlot = state.yourSlot || 0;
        const width = 62;
        const healthRatio = Math.max(0, Math.min(1, plane.health / plane.maxHealth));
        const barColor = healthRatio > 0.55 ? "#4ade80" : healthRatio > 0.25 ? "#facc15" : "#f87171";
        const isLocal = plane.slot === yourSlot;
        const label = plane.isBot ? "БОТ" : isLocal ? "ВЫ" : "СОПЕРНИК";
        const labelColor = isLocal ? "#bae6fd" : "#fecaca";

        ctx.fillStyle = "rgba(2, 6, 23, 0.65)";
        ctx.fillRect(plane.x - width / 2, plane.y - 48, width, 7);
        ctx.fillStyle = barColor;
        ctx.fillRect(plane.x - width / 2 + 1, plane.y - 47, (width - 2) * healthRatio, 5);

        if (plane.kind === "biplane" && plane.reload > 0 && plane.reloadDuration > 0) {
            const reloadWidth = 62;
            const progress = Math.max(0, Math.min(1, 1 - plane.reload / plane.reloadDuration));

            ctx.fillStyle = "rgba(2, 6, 23, 0.72)";
            pathRoundRect(ctx, plane.x - reloadWidth / 2, plane.y - 59, reloadWidth, 5, 2);
            ctx.fill();
            ctx.fillStyle = "#facc15";
            pathRoundRect(
                ctx,
                plane.x - reloadWidth / 2 + 1,
                plane.y - 58,
                (reloadWidth - 2) * progress,
                3,
                1);
            ctx.fill();
        }

        const abilityCooldown = plane.kind === "biplane"
            ? (plane.boostCooldown || 0)
            : (plane.dashCooldown || 0);
        const abilityDuration = plane.abilityCooldownDuration || 1;

        if (abilityCooldown > 0) {
            const abilityWidth = 54;
            const progress = Math.max(0, Math.min(1, 1 - abilityCooldown / abilityDuration));

            ctx.fillStyle = "rgba(2, 6, 23, 0.72)";
            pathRoundRect(ctx, plane.x - abilityWidth / 2, plane.y + 52, abilityWidth, 5, 2);
            ctx.fill();
            ctx.fillStyle = "#facc15";
            pathRoundRect(
                ctx,
                plane.x - abilityWidth / 2 + 1,
                plane.y + 53,
                (abilityWidth - 2) * progress,
                3,
                1);
            ctx.fill();
        }

        ctx.font = "600 13px 'Segoe UI', sans-serif";
        ctx.textAlign = "center";
        ctx.lineWidth = 3;
        ctx.strokeStyle = palette.ink;
        ctx.strokeText(label, plane.x, plane.y - 55);
        ctx.fillStyle = labelColor;
        ctx.fillText(label, plane.x, plane.y - 55);

        if (plane.abandoned) {
            ctx.font = "600 10px 'Segoe UI', sans-serif";
            ctx.fillStyle = "rgba(238, 247, 252, 0.9)";
            ctx.lineWidth = 3;
            ctx.strokeStyle = palette.ink;
            ctx.strokeText("БЕЗ ЛЁТЧИКА", plane.x, plane.y + 62);
            ctx.fillText("БЕЗ ЛЁТЧИКА", plane.x, plane.y + 62);
        }

        if (plane.protection > 0) {
            ctx.save();
            ctx.strokeStyle = "rgba(125, 211, 252, 0.8)";
            ctx.lineWidth = 2;
            ctx.setLineDash([6, 6]);
            ctx.beginPath();
            ctx.arc(plane.x, plane.y, 37, 0, Math.PI * 2);
            ctx.stroke();
            ctx.restore();
        }

        if (plane.kind === "biplane") {
            drawAmmo(ctx, plane);
        }
    }

    function drawAmmo(ctx, plane) {
        if (!plane.magazine) {
            return;
        }

        const pipRadius = 2.6;
        const gap = 8.5;
        const total = plane.magazine;
        const startX = plane.x - ((total - 1) * gap) / 2;
        const y = plane.y - 64;

        for (let i = 0; i < total; i += 1) {
            ctx.beginPath();
            ctx.arc(startX + i * gap, y, pipRadius, 0, Math.PI * 2);
            ctx.fillStyle = i < plane.ammo ? "#e2e8f0" : "rgba(148, 163, 184, 0.28)";
            ctx.fill();
        }
    }

    function drawPilot(ctx, game, pilot, state) {
        if (pilot.state === "inplane" || pilot.state === "waiting") {
            return;
        }

        const yourSlot = state.yourSlot || 0;
        const color = slotColor(pilot.slot, yourSlot);
        const facing = pilot.facing >= 0 ? 1 : -1;
        const x = pilot.x;
        const y = pilot.y;

        ctx.save();

        if (pilot.state === "parachuting") {
            const sway = Math.sin((state.elapsed || 0) * 2.2 + pilot.slot) * 3;

            ctx.strokeStyle = "rgba(226, 232, 240, 0.55)";
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(x - 16, y - 30);
            ctx.lineTo(x - 4, y - 8);
            ctx.moveTo(x + 16, y - 30);
            ctx.lineTo(x + 4, y - 8);
            ctx.stroke();

            ctx.fillStyle = color;
            ctx.globalAlpha = 0.92;
            ctx.beginPath();
            ctx.ellipse(x + sway, y - 33, 22, 10, 0, Math.PI, 0);
            ctx.fill();
            ctx.globalAlpha = 1;

            ctx.strokeStyle = "rgba(15, 23, 42, 0.55)";
            ctx.lineWidth = 1.5;
            ctx.beginPath();
            ctx.ellipse(x + sway, y - 33, 22, 10, 0, Math.PI, 0);
            ctx.stroke();
        }

        if (pilot.state === "falling") {
            const vy = pilot.vy || 0;
            const safe = state.safeLandingSpeed || 240;
            const danger = Math.max(0, Math.min(1, vy / (safe * 2)));
            const lines = 1 + Math.round(danger * 3);

            ctx.strokeStyle = vy > safe ? "rgba(248, 113, 113, 0.85)" : "rgba(74, 222, 128, 0.85)";
            ctx.lineWidth = 2;

            for (let i = 0; i < lines; i += 1) {
                const offset = i * 7;

                ctx.beginPath();
                ctx.moveTo(x - 8, y - 16 - offset);
                ctx.lineTo(x, y - 8 - offset);
                ctx.lineTo(x + 8, y - 16 - offset);
                ctx.stroke();
            }
        }

        ctx.translate(x, y);
        ctx.scale(facing, 1);

        const halo = Math.max(pilotRadius * 1.6, 13 / game.view.scale);
        ctx.fillStyle = "rgba(248, 250, 252, 0.16)";
        ctx.beginPath();
        ctx.arc(0, -pilotRadius * 0.2, halo, 0, Math.PI * 2);
        ctx.fill();

        ctx.fillStyle = "#f8fafc";
        ctx.beginPath();
        ctx.arc(0, -pilotRadius * 0.9, pilotRadius * 0.55, 0, Math.PI * 2);
        ctx.fill();

        ctx.fillStyle = color;
        pathRoundRect(ctx, -pilotRadius * 0.5, -pilotRadius * 0.35, pilotRadius, pilotRadius * 1.7, 3);
        ctx.fill();

        if (pilot.state === "onground") {
            ctx.strokeStyle = color;
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.moveTo(-2, pilotRadius * 1.25);
            ctx.lineTo(-6, pilotRadius * 2.1);
            ctx.moveTo(2, pilotRadius * 1.25);
            ctx.lineTo(6, pilotRadius * 2.1);
            ctx.stroke();
        }

        ctx.restore();
    }

    function drawParticles(ctx, particles) {
        for (const particle of particles) {
            ctx.globalAlpha = Math.max(0, Math.min(1, particle.alpha));
            ctx.fillStyle = particle.color;
            ctx.beginPath();
            ctx.arc(particle.x, particle.y, Math.max(0.5, particle.size), 0, Math.PI * 2);
            ctx.fill();
        }

        ctx.globalAlpha = 1;
    }

    function drawHud(game, state) {
        const ctx = game.ctx;
        const yourSlot = state.yourSlot || 0;

        ctx.save();

        ctx.restore();
    }

    function drawPilotHud(ctx, game, state) {
        const x = game.cssWidth - 252;
        const y = game.cssHeight - 106;
        const width = 234;
        const height = 88;

        ctx.fillStyle = "rgba(2, 6, 23, 0.62)";
        pathRoundRect(ctx, x, y, width, height, 12);
        ctx.fill();
        ctx.strokeStyle = "rgba(148, 163, 184, 0.2)";
        ctx.lineWidth = 1;
        ctx.stroke();

        const yourSlot = state.yourSlot || 0;
        const pilot = (state.pilots || []).find((entry) => entry.slot === yourSlot);
        const pilotState = pilot ? pilot.state : "waiting";
        const vy = pilot && typeof pilot.vy === "number" ? Math.max(0, pilot.vy) : 0;
        const safe = state.safeLandingSpeed || 240;
        const tooFast = vy > safe;

        let title;
        let first;
        let second;
        let speedColor = "#94a3b8";

        if (pilotState === "falling") {
            title = "СВОБОДНОЕ ПАДЕНИЕ";
            first = pilot && pilot.parachuteBroken
                ? "Парашют уничтожен до респавна"
                : "Пробел — раскрыть парашют";
            second = "A / D — планирование";
            speedColor = tooFast ? "#f87171" : "#4ade80";
        } else if (pilotState === "parachuting") {
            title = "ПАРАШЮТ";
            first = "A / D — управление";
            second = "Пробел — сложить · E — в аппарат";
            speedColor = "#4ade80";
        } else if (pilotState === "onground") {
            title = "ЛЁТЧИК НА ЗЕМЛЕ";
            first = "Бегите к башне-маяку";
            second = "E — вернуться в аппарат";
        } else {
            title = "ВОЗРОЖДЕНИЕ";
            first = "Аппарат разбит";
            second = "Новый аппарат у башни";
        }

        ctx.textAlign = "left";
        ctx.font = "600 12px 'Segoe UI', sans-serif";
        ctx.fillStyle = "#94a3b8";
        ctx.fillText(title, x + 14, y + 19);

        ctx.fillStyle = "#e2e8f0";
        ctx.fillText(first, x + 14, y + 39);
        ctx.fillStyle = "#7dd3fc";
        ctx.fillText(second, x + 14, y + 57);

        if (pilotState === "falling" || pilotState === "parachuting") {
            ctx.fillStyle = speedColor;
            ctx.fillText(
                `Снижение: ${Math.round(vy)} / норма ${Math.round(safe)}`,
                x + 14,
                y + 77);
        }
    }

    function handleEvents(game, state) {
        const yourSlot = state.yourSlot || 0;

        for (const event of state.events || []) {
            const color = slotColor(event.slot, yourSlot);

            switch (event.type) {
                case "shot":
                    spawnParticles(game, event.x, event.y, 3, color, 90, 0.18, 2.5);
                    break;
                case "hit":
                    spawnParticles(game, event.x, event.y, 10, "#fbbf24", 170, 0.35, 3);
                    break;
                case "explosion":
                    spawnParticles(game, event.x, event.y, 34, "#fb923c", 300, 0.8, 4.5);
                    spawnParticles(game, event.x, event.y, 12, "#fde68a", 180, 0.6, 3.5);
                    break;
                case "crash":
                    spawnParticles(game, event.x, event.y, 24, "#94a3b8", 220, 0.7, 4);
                    break;
                case "boost":
                    spawnParticles(game, event.x, event.y, 12, "#22d3ee", 180, 0.45, 3);
                    break;
                case "dash":
                    spawnParticles(game, event.x, event.y, 10, "#a7f3d0", 220, 0.35, 3);
                    break;
                case "respawn":
                case "towerReached":
                    spawnParticles(game, event.x, event.y, 18, "#facc15", 150, 0.6, 3.5);
                    break;
                case "reloadStarted":
                    spawnParticles(game, event.x, event.y, 4, "#facc15", 60, 0.3, 2.5);
                    break;
                case "reloadFinished":
                    spawnParticles(game, event.x, event.y, 8, "#e2e8f0", 100, 0.35, 2.5);
                    break;
                case "eject":
                case "reenter":
                    spawnParticles(game, event.x, event.y, 10, color, 130, 0.4, 3);
                    break;
                case "landed":
                    spawnParticles(game, event.x, event.y, 8, "#4ade80", 110, 0.4, 2.5);
                    break;
                case "parachuteOpened":
                    spawnParticles(game, event.x, event.y - 26, 14, "#e2e8f0", 130, 0.5, 3);
                    break;
                case "parachuteClosed":
                    spawnParticles(game, event.x, event.y - 26, 8, "#94a3b8", 90, 0.35, 2.5);
                    break;
                case "parachuteDestroyed":
                    spawnParticles(game, event.x, event.y, 16, "#f87171", 170, 0.5, 3);
                    spawnParticles(game, event.x, event.y, 8, "#e2e8f0", 110, 0.4, 2.5);
                    break;
                case "pilotKilled":
                    spawnParticles(game, event.x, event.y, 22, "#f87171", 210, 0.6, 3.5);
                    spawnParticles(game, event.x, event.y, 8, "#fecaca", 120, 0.45, 2.5);
                    break;
                default:
                    break;
            }
        }
    }

    function spawnParticles(game, x, y, count, color, speed, life, size) {
        for (let i = 0; i < count; i += 1) {
            const angle = Math.random() * Math.PI * 2;
            const velocity = speed * (0.25 + Math.random() * 0.75);
            const particleLife = life * (0.6 + Math.random() * 0.4);

            game.particles.push({
                x,
                y,
                vx: Math.cos(angle) * velocity,
                vy: Math.sin(angle) * velocity,
                life: particleLife,
                maxLife: particleLife,
                alpha: 1,
                size: size * (0.6 + Math.random() * 0.4),
                color
            });
        }

        if (game.particles.length > 600) {
            game.particles.splice(0, game.particles.length - 600);
        }
    }

    function updateParticles(game, dt) {
        for (let i = game.particles.length - 1; i >= 0; i -= 1) {
            const particle = game.particles[i];
            particle.life -= dt;

            if (particle.life <= 0) {
                game.particles.splice(i, 1);
                continue;
            }

            particle.x += particle.vx * dt;
            particle.y += particle.vy * dt;
            particle.vx *= Math.max(0, 1 - 3 * dt);
            particle.vy *= Math.max(0, 1 - 3 * dt);
            particle.alpha = particle.life / particle.maxLife;
        }
    }

    function resetTouchInput(game) {
        game.touch.moveX = 0;
        game.touch.moveY = 0;
        game.touch.aimX = 0;
        game.touch.aimY = 0;
        game.touch.aimActive = false;
        game.touch.fire = false;
        game.touch.thrust = 0;
        game.abilityPressed = false;

        game.touchUi?.stick?.classList.remove("is-active");
        game.touchUi?.throwStick?.classList.remove("is-active");
        game.touchUi?.steerStick?.classList.remove("is-active");
        document.querySelectorAll(".touch-stick-knob").forEach((knob) => {
            knob.style.transform = "";
        });
    }

    function isPterodactyl(game) {
        const state = game.state;
        if (!state) {
            return false;
        }

        const plane = (state.planes || []).find((item) => item.slot === state.yourSlot);
        const pilot = (state.pilots || []).find((item) => item.slot === state.yourSlot);
        return (plane?.kind || pilot?.kind) === "pterodactyl";
    }

    function attachTouchControls(game) {
        const container = document.querySelector(".touch-controls");

        if (!container) {
            return;
        }

        const cleanups = [];
        const zone = container.querySelector("[data-touch-zone='move']");
        const stick = zone?.querySelector("[data-touch-stick='move']");
        const knob = stick?.querySelector(".touch-stick-knob");
        const throwZone = container.querySelector("[data-touch-zone='throw']");
        const throwStick = throwZone?.querySelector("[data-touch-stick='throw']");
        const throwArrow = throwStick?.querySelector(".throw-arrow");
        const steerZone = container.querySelector("[data-touch-zone='steer']");
        const steerStick = steerZone?.querySelector("[data-touch-stick='steer']");
        const steerKnob = steerStick?.querySelector(".touch-stick-knob");
        const contextButton = container.querySelector("[data-touch-action='context']");
        const reloadButton = container.querySelector("[data-touch-action='reload']");
        const abilityButton = container.querySelector("[data-touch-action='ability']");

        game.touchUi = {
            container,
            stick,
            context: contextButton,
            reload: reloadButton,
            ability: abilityButton,
            throwStick,
            steerStick,
            arrow: throwArrow || null
        };

        if (zone && stick) {
            let pointerId = null;
            let centerX = 0;
            let centerY = 0;
            let radius = 1;

            const update = (event) => {
                let dx = event.clientX - centerX;
                let dy = event.clientY - centerY;
                const length = Math.hypot(dx, dy);

                if (length > radius) {
                    dx = (dx / length) * radius;
                    dy = (dy / length) * radius;
                }

                if (knob) {
                    knob.style.transform = `translate(${dx}px, ${dy}px)`;
                }

                const x = dx / radius;
                const y = dy / radius;
                const active = Math.hypot(x, y) > 0.12;
                game.touch.moveX = active ? x : 0;
                game.touch.moveY = active ? y : 0;
            };

            const reset = () => {
                pointerId = null;
                stick.classList.remove("is-active");

                if (knob) {
                    knob.style.transform = "";
                }

                game.touch.moveX = 0;
                game.touch.moveY = 0;
            };

            const onDown = (event) => {
                if (pointerId !== null) {
                    return;
                }

                pointerId = event.pointerId;
                centerX = event.clientX;
                centerY = event.clientY;

                const zoneRect = zone.getBoundingClientRect();
                stick.style.left = `${centerX - zoneRect.left}px`;
                stick.style.top = `${centerY - zoneRect.top}px`;
                radius = Math.max(1, stick.getBoundingClientRect().width * 0.36);
                stick.classList.add("is-active");
                zone.setPointerCapture?.(event.pointerId);
                update(event);
                event.preventDefault();
            };

            const onMove = (event) => {
                if (event.pointerId !== pointerId) {
                    return;
                }

                update(event);
                event.preventDefault();
            };

            const onUp = (event) => {
                if (event.pointerId === pointerId) {
                    reset();
                }
            };

            const onCancel = (event) => {
                if (event.pointerId === pointerId) {
                    reset();
                }
            };

            zone.addEventListener("pointerdown", onDown);
            zone.addEventListener("pointermove", onMove);
            zone.addEventListener("pointerup", onUp);
            zone.addEventListener("pointercancel", onCancel);
            zone.addEventListener("lostpointercapture", onCancel);

            cleanups.push(() => {
                zone.removeEventListener("pointerdown", onDown);
                zone.removeEventListener("pointermove", onMove);
                zone.removeEventListener("pointerup", onUp);
                zone.removeEventListener("pointercancel", onCancel);
                zone.removeEventListener("lostpointercapture", onCancel);
            });
        }

        if (steerZone && steerStick) {
            let pointerId = null;
            let centerX = 0;
            let radius = 1;

            const update = (event) => {
                const rect = steerStick.getBoundingClientRect();
                radius = Math.max(1, rect.width * 0.4);
                const dx = clamp(event.clientX - centerX, -radius, radius);

                if (steerKnob) {
                    steerKnob.style.transform = `translate(${dx}px, 0px)`;
                }

                game.touch.moveX = Math.abs(dx / radius) > 0.12 ? dx / radius : 0;
            };

            const reset = () => {
                pointerId = null;
                steerStick.classList.remove("is-active");

                if (steerKnob) {
                    steerKnob.style.transform = "";
                }

                game.touch.moveX = 0;
            };

            const onDown = (event) => {
                if (pointerId !== null) {
                    return;
                }

                pointerId = event.pointerId;
                centerX = event.clientX;
                const zoneRect = steerZone.getBoundingClientRect();
                steerStick.style.left = `${centerX - zoneRect.left}px`;
                steerStick.classList.add("is-active");
                steerZone.setPointerCapture?.(event.pointerId);
                update(event);
                event.preventDefault();
            };

            const onMove = (event) => {
                if (event.pointerId === pointerId) {
                    update(event);
                    event.preventDefault();
                }
            };

            const onUp = (event) => {
                if (event.pointerId === pointerId) {
                    reset();
                }
            };

            steerZone.addEventListener("pointerdown", onDown);
            steerZone.addEventListener("pointermove", onMove);
            steerZone.addEventListener("pointerup", onUp);
            steerZone.addEventListener("pointercancel", onUp);
            steerZone.addEventListener("lostpointercapture", onUp);

            cleanups.push(() => {
                steerZone.removeEventListener("pointerdown", onDown);
                steerZone.removeEventListener("pointermove", onMove);
                steerZone.removeEventListener("pointerup", onUp);
                steerZone.removeEventListener("pointercancel", onUp);
                steerZone.removeEventListener("lostpointercapture", onUp);
            });
        }

        const updateThrowAim = (event) => {
            if (!throwStick) {
                return;
            }

            const rect = throwStick.getBoundingClientRect();
            const dx = event.clientX - (rect.left + rect.width / 2);
            const dy = event.clientY - (rect.top + rect.height / 2);

            if (Math.hypot(dx, dy) < 6) {
                return;
            }

            const state = game.state;
            const plane = state && (state.planes || []).find(
                (item) => item.slot === state.yourSlot);

            if (!plane) {
                return;
            }

            game.touch.aimX = plane.x + dx * 10;
            game.touch.aimY = plane.y + dy * 10;
            game.touch.aimActive = true;
        };

        if (throwZone && throwStick) {
            let pointerId = null;

            const reset = () => {
                pointerId = null;
                throwStick.classList.remove("is-active");
                game.touch.fire = false;
                game.touch.aimActive = false;
            };

            const onDown = (event) => {
                if (pointerId !== null) {
                    return;
                }

                pointerId = event.pointerId;
                const zoneRect = throwZone.getBoundingClientRect();
                throwStick.style.left = `${event.clientX - zoneRect.left}px`;
                throwStick.style.top = `${event.clientY - zoneRect.top}px`;
                throwStick.classList.add("is-active");
                throwZone.setPointerCapture?.(event.pointerId);
                game.touch.fire = true;
                updateThrowAim(event);
                window.myakTelegram?.haptic?.("selectionChanged");
                event.preventDefault();
            };

            const onMove = (event) => {
                if (event.pointerId !== pointerId) {
                    return;
                }

                updateThrowAim(event);
                event.preventDefault();
            };

            const onUp = (event) => {
                if (event.pointerId === pointerId) {
                    reset();
                }
            };

            throwZone.addEventListener("pointerdown", onDown);
            throwZone.addEventListener("pointermove", onMove);
            throwZone.addEventListener("pointerup", onUp);
            throwZone.addEventListener("pointercancel", onUp);
            throwZone.addEventListener("lostpointercapture", onUp);

            cleanups.push(() => {
                throwZone.removeEventListener("pointerdown", onDown);
                throwZone.removeEventListener("pointermove", onMove);
                throwZone.removeEventListener("pointerup", onUp);
                throwZone.removeEventListener("pointercancel", onUp);
                throwZone.removeEventListener("lostpointercapture", onUp);
            });
        }

        container.querySelectorAll("[data-touch-action]").forEach((button) => {
            const action = button.dataset.touchAction;
            let pointerId = null;

            const onDown = (event) => {
                if (pointerId !== null) {
                    return;
                }

                pointerId = event.pointerId;
                button.setPointerCapture?.(event.pointerId);
                event.preventDefault();
                window.myakTelegram?.haptic?.("selectionChanged");

                switch (action) {
                    case "fire":
                        game.touch.fire = true;

                        if (isPterodactyl(game)) {
                            updateThrowAim(event);
                        }

                        break;
                    case "ability":
                        game.abilityPressed = true;
                        break;
                    case "gas":
                        game.touch.thrust = 1;
                        break;
                    case "reload":
                        game.reloadPressed = true;
                        break;
                    case "context": {
                        const mode = button.dataset.touchState;

                        if (mode === "eject" || mode === "reenter") {
                            game.ejectPressed = true;
                        } else if (mode === "open" || mode === "close") {
                            game.parachutePressed = true;
                        }

                        break;
                    }
                    case "pause":
                        game.dotNetRef.invokeMethodAsync("TogglePause");
                        break;
                    default:
                        break;
                }
            };

            const onMove = (event) => {
                if (event.pointerId !== pointerId) {
                    return;
                }

                if (action === "fire" && isPterodactyl(game)) {
                    updateThrowAim(event);
                    event.preventDefault();
                }
            };

            const release = (event) => {
                if (event.pointerId !== pointerId) {
                    return;
                }

                pointerId = null;

                if (action === "fire") {
                    game.touch.fire = false;
                    game.touch.aimActive = false;
                }

                if (action === "gas") {
                    game.touch.thrust = 0;
                }
            };

            button.addEventListener("pointerdown", onDown);
            button.addEventListener("pointermove", onMove);
            button.addEventListener("pointerup", release);
            button.addEventListener("pointercancel", release);
            button.addEventListener("lostpointercapture", release);

            cleanups.push(() => {
                button.removeEventListener("pointerdown", onDown);
                button.removeEventListener("pointermove", onMove);
                button.removeEventListener("pointerup", release);
                button.removeEventListener("pointercancel", release);
                button.removeEventListener("lostpointercapture", release);
            });
        });

        game.detachTouchControls = () => {
            cleanups.forEach((cleanup) => cleanup());
            game.touchUi = null;
            game.detachTouchControls = null;
        };
    }

    function updateTouchUi(game, state) {
        const ui = game.touchUi;

        if (!ui || !state) {
            return;
        }

        const slot = state.yourSlot;
        const pilot = (state.pilots || []).find((item) => item.slot === slot);
        const ownPlane = (state.planes || []).find(
            (item) => item.slot === slot && item.alive && !item.abandoned);
        const kind = ownPlane?.kind || pilot?.kind || "biplane";

        if (ui.container.dataset.kind !== kind) {
            ui.container.dataset.kind = kind;
        }

        let mode = "eject";
        let label = "Покинуть аппарат";

        if (pilot && pilot.state !== "inplane") {
            const canReenter = (state.planes || []).some((plane) =>
                plane.slot === slot
                && plane.alive
                && plane.abandoned
                && Math.hypot(plane.x - pilot.x, plane.y - pilot.y) < 90);

            if (canReenter) {
                mode = "reenter";
                label = "Вернуться в аппарат";
            } else if (pilot.state === "parachuting") {
                mode = "close";
                label = "Сложить парашют";
            } else if (pilot.parachuteBroken) {
                mode = "disabled";
                label = "Купол повреждён";
            } else {
                mode = "open";
                label = "Раскрыть парашют";
            }
        }

        const modeKey = `${mode}|${kind}`;

        if (game.touchMode !== modeKey && ui.context) {
            ui.context.dataset.touchState = mode;
            ui.context.className = `touch-button touch-context touch-action-${mode}`;
            ui.context.setAttribute("aria-label", label);
            game.touchMode = modeKey;
        }

        if (ui.reload) {
            const loaded = kind === "biplane"
                && !!ownPlane
                && ownPlane.ammo > 0
                && !(ownPlane.reload > 0);

            ui.reload.style.display = kind === "biplane" && ownPlane ? "flex" : "none";
            ui.reload.classList.toggle("touch-reload-empty", !loaded);
            ui.reload.setAttribute(
                "aria-label",
                loaded ? `Перезарядка, патронов ${ownPlane.ammo}` : "Магазин пуст");
        }

        if (ui.arrow && kind === "pterodactyl" && ownPlane) {
            const angle = game.touch.aimActive
                ? Math.atan2(game.touch.aimY - ownPlane.y, game.touch.aimX - ownPlane.x)
                : (ownPlane.angle || 0);
            const degrees = Math.round((angle * 180) / Math.PI);
            const power = Math.max(0, Math.min(1, ownPlane.charge || 0));
            const powerKey = Math.round(power * 100);

            if (game.touchAngle !== degrees) {
                ui.arrow.style.setProperty("--throw-angle", `${degrees}deg`);
                game.touchAngle = degrees;
            }

            if (game.touchPower !== powerKey) {
                ui.arrow.style.setProperty("--throw-power", String(power));
                game.touchPower = powerKey;
            }
        }
    }

    function attachInput(game) {
        const canvas = game.canvas;

        const onKeyDown = (event) => {
            if (["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Space"].includes(event.code)) {
                event.preventDefault();
            }

            if (event.code === "Escape") {
                event.preventDefault();

                if (!event.repeat) {
                    game.dotNetRef.invokeMethodAsync("TogglePause");
                }

                return;
            }

            if (event.code === "KeyR" && !event.repeat) {
                game.reloadPressed = true;
            }

            if (event.code === "KeyE" && !event.repeat) {
                game.ejectPressed = true;
            }

            if (event.code === "Space" && !event.repeat) {
                game.parachutePressed = true;
            }

            game.keys.add(event.code);
        };

        const onKeyUp = (event) => {
            game.keys.delete(event.code);
        };

        const onBlur = () => {
            game.keys.clear();
            game.mouse.down = false;
            resetTouchInput(game);
        };

        const onPointerMove = (event) => {
            updatePointer(game, event);
            game.mouse.aimed = true;
        };

        const onPointerDown = (event) => {
            if (event.button !== 0) {
                return;
            }

            event.preventDefault();
            updatePointer(game, event);
            game.mouse.aimed = true;
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

        if (!handler) {
            return;
        }

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

    function start(dotNetRef, canvasId) {
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
            keys: new Set(),
            mouse: { worldX: 800, worldY: 450, down: false, aimed: false },
            touch: {
                moveX: 0,
                moveY: 0,
                aimX: 0,
                aimY: 0,
                aimActive: false,
                fire: false,
                thrust: 0
            },
            view: { worldWidth: 1600, worldHeight: 900, scale: 1, offsetX: 0, offsetY: 0 },
            cssWidth: 1,
            cssHeight: 1,
            running: true,
            reloadPressed: false,
            ejectPressed: false,
            parachutePressed: false,
            abilityPressed: false,
            lastTimestamp: 0,
            particles: [],
            rafId: 0,
            touchUi: null,
            handlers: {}
        };

        games.set(canvasId, game);
        resize(game);
        attachInput(game);
        attachTouchControls(game);
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
        game.detachTouchControls?.();
        games.delete(canvasId);
    }

    async function copyText(text) {
        if (!text) {
            return;
        }

        try {
            if (navigator.clipboard?.writeText) {
                await navigator.clipboard.writeText(text);
                return;
            }
        } catch (error) {
            console.warn("MyaKBiplane: clipboard unavailable", error);
        }

        const area = document.createElement("textarea");
        area.value = text;
        area.style.position = "fixed";
        area.style.opacity = "0";
        document.body.appendChild(area);
        area.select();
        document.execCommand("copy");
        document.body.removeChild(area);
    }

    window.myakBiplane = {
        start,
        stop,
        copyText,
        getState: (canvasId) => {
            const game = games.get(canvasId);
            return game && game.state ? game.state : null;
        }
    };
})();
