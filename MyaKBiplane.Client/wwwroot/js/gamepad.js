/*
 * Поддержка Xbox/PS-геймпадов через Gamepad API.
 * В бою состояние читает game.js, в менюклавиши/стик двигают фокус и нажимают кнопки.
 */
(() => {
    "use strict";

    const deadZone = 0.18;
    let state = { active: false, axes: [0, 0, 0, 0], buttons: [] };
    let previousButtons = [];
    let previousStickY = 0;
    let focusIndex = -1;

    const pressed = (index) => !!(state.buttons[index] && !previousButtons[index]);

    function axis(value) {
        const v = Number.isFinite(value) ? value : 0;
        return Math.abs(v) < deadZone ? 0 : v;
    }

    function focusable() {
        const menu = document.querySelector(".menu-card");

        if (!menu) {
            return [];
        }

        return Array.from(menu.querySelectorAll(
            "button:not([disabled]), a[href], input:not([disabled]), textarea:not([disabled])"
        )).filter((element) => element.offsetParent !== null);
    }

    function moveFocus(direction) {
        const elements = focusable();

        if (!elements.length) {
            return;
        }

        focusIndex = focusIndex < 0
            ? 0
            : (focusIndex + direction + elements.length) % elements.length;
        elements[focusIndex].focus();
    }

    function handleMenu() {
        if (document.querySelector("canvas#gameCanvas")) {
            return;
        }

        if (pressed(12) || (state.axes[1] < -0.6 && previousStickY >= -0.6)) {
            moveFocus(-1);
        }

        if (pressed(13) || (state.axes[1] > 0.6 && previousStickY <= 0.6)) {
            moveFocus(1);
        }

        previousStickY = state.axes[1];

        if (pressed(0)) {
            const active = document.activeElement;

            if (active && active.click && document.querySelector(".menu-card")?.contains(active)) {
                active.click();
            }
        }

        if (pressed(1)) {
            const back = Array.from(document.querySelectorAll(".menu-card button"))
                .find((button) => /назад|меню|главную/i.test(button.textContent || ""));
            back?.click();
        }
    }

    function poll() {
        const pads = navigator.getGamepads ? navigator.getGamepads() : [];
        const pad = Array.from(pads).find((item) => item && item.connected) || null;

        state = {
            active: !!pad,
            axes: pad
                ? [axis(pad.axes[0]), axis(pad.axes[1]), axis(pad.axes[2]), axis(pad.axes[3])]
                : [0, 0, 0, 0],
            buttons: pad ? pad.buttons.map((button) => !!button.pressed) : []
        };

        document.documentElement.classList.toggle("has-gamepad", state.active);

        if (state.active) {
            handleMenu();
        }

        previousButtons = state.buttons.slice();
        window.requestAnimationFrame(poll);
    }

    window.myakGamepad = {
        isActive: () => state.active,
        read: () => state
    };

    window.addEventListener("gamepadconnected", () => {
        document.documentElement.classList.add("has-gamepad");
    });

    window.addEventListener("gamepaddisconnected", () => {
        document.documentElement.classList.remove("has-gamepad");
        focusIndex = -1;
    });

    window.requestAnimationFrame(poll);
})();
