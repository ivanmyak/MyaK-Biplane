(function () {
    "use strict";

    const root = document.documentElement;
    const tg = window.Telegram?.WebApp;
    const coarsePointer = !!window.matchMedia?.("(pointer: coarse)").matches;

    // SDK подключается и вне Telegram, но initData и платформа остаются пустыми.
    // Поэтому «настоящим» Mini App считаем только контекст с данными Telegram.
    const inTelegram = !!tg
        && (!!tg.initData
            || !!tg.initDataUnsafe?.user
            || (!!tg.platform && tg.platform !== "unknown"));
    const telegramMobile = inTelegram && (tg.isMobile === true
        || ["android", "ios"].includes((tg.platform || "").toLowerCase()));
    const touchView = telegramMobile || coarsePointer;

    function ensureRotateOverlay() {
        if (!touchView || document.documentElement.querySelector(".telegram-rotate-overlay")) {
            return;
        }

        const overlay = document.createElement("div");
        overlay.className = "telegram-rotate-overlay";
        overlay.setAttribute("role", "status");
        overlay.innerHTML =
            '<div class="telegram-rotate-icon" aria-hidden="true">↻</div>'
            + '<p class="telegram-rotate-title">Поверните устройство</p>'
            + '<p class="telegram-rotate-hint">Игра работает только в горизонтальной ориентации.<br />'
            + 'Разверните телефон — и поле боя откроется на весь экран.</p>';

        // Скрипт подключён в <head>, где document.body ещё может не существовать.
        document.documentElement.appendChild(overlay);
    }

    const api = {
        isAvailable: () => inTelegram,
        isMobile: () => telegramMobile,
        displayName: () => {
            const user = tg?.initDataUnsafe?.user;

            if (!user) {
                return "";
            }

            return [user.first_name, user.last_name].filter(Boolean).join(" ")
                || (user.username ? "@" + user.username : "");
        },
        initData: inTelegram ? (tg.initData || "") : "",
        platform: inTelegram ? (tg.platform || "") : "",
        version: inTelegram ? (tg.version || "") : "",
        setGameActive: () => {},
        setScrollLocked: () => {},
        setBackButton: () => {},
        haptic: () => {}
    };

    window.myakTelegram = api;

    if (!inTelegram) {
        // На сенсорном устройстве вне Telegram всё равно держим экран
        // горизонтальным — игра рассчитана именно на такой формат.
        if (touchView) {
            ensureRotateOverlay();
            root.classList.toggle("needs-landscape", window.innerHeight > window.innerWidth);
            window.addEventListener("resize", () => {
                root.classList.toggle("needs-landscape", window.innerHeight > window.innerWidth);
            });
        }

        return;
    }

    root.classList.add("telegram-webapp");

    if (api.isMobile()) {
        root.classList.add("telegram-mobile");
    }

    root.dataset.tgPlatform = api.platform;
    root.dataset.tgVersion = api.version;

    function menuUrl() {
        try {
            return new URL(".", document.baseURI).href;
        } catch (error) {
            return "/";
        }
    }

    function applyViewport() {
        const height = tg.viewportStableHeight || tg.viewportHeight || window.innerHeight;
        const safe = tg.safeAreaInset || {};
        const content = tg.contentSafeAreaInset || {};

        root.style.setProperty("--tg-viewport-height", `${Math.round(height)}px`);
        root.style.setProperty("--tg-safe-top", `${(safe.top || 0) + (content.top || 0)}px`);
        root.style.setProperty("--tg-safe-right", `${(safe.right || 0) + (content.right || 0)}px`);
        root.style.setProperty("--tg-safe-bottom", `${(safe.bottom || 0) + (content.bottom || 0)}px`);
        root.style.setProperty("--tg-safe-left", `${(safe.left || 0) + (content.left || 0)}px`);
    }

    function applyTheme() {
        // Пользовательский выбор темы в приложении важнее темы Telegram.
        if (!window.myakTheme?.stored?.()) {
            window.myakTheme?.apply?.(tg.colorScheme === "light" ? "light" : "dark");
        }
    }

    function isPortrait() {
        return window.innerHeight > window.innerWidth;
    }

    function lockLandscape() {
        if (!touchView || isPortrait()) {
            return;
        }

        // lockOrientation фиксирует ТЕКУЩУЮ ориентацию, поэтому вызываем его
        // только когда устройство уже повёрнуто в ландшафт (Bot API 8.0+).
        if (inTelegram) {
            if (typeof tg.lockOrientation === "function" && tg.isOrientationLocked !== true) {
                try {
                    tg.lockOrientation();
                } catch (error) {
                    // Старые клиенты Telegram не поддерживают блокировку.
                }
            }

            return;
        }

        // Обычный браузер разрешает screen.orientation.lock только в fullscreen.
        if (document.fullscreenElement && screen.orientation?.lock) {
            try {
                const result = screen.orientation.lock("landscape");
                result?.catch?.(() => {});
            } catch (error) {
                // Поддержка API зависит от браузера.
            }
        }
    }

    function updateOrientation() {
        if (!touchView) {
            return;
        }

        root.classList.toggle("needs-landscape", isPortrait());

        if (isPortrait()) {
            // Если ориентация уже зафиксирована в портрете, снимаем блокировку,
            // иначе пользователь физически не сможет повернуть устройство.
            if (tg.isOrientationLocked === true && typeof tg.unlockOrientation === "function") {
                try {
                    tg.unlockOrientation();
                } catch (error) {
                    // Блокировки могло и не быть.
                }
            }

            return;
        }

        lockLandscape();
    }

    let backButtonBound = false;

    function onBackButton() {
        window.location.assign(menuUrl());
    }

    api.setGameActive = (active) => {
        try {
            if (active) {
                tg.enableClosingConfirmation?.();
                tg.requestFullscreen?.();
                lockLandscape();
            } else {
                tg.disableClosingConfirmation?.();
            }
        } catch (error) {
            // Старые версии Telegram не поддерживают часть методов.
        }
    };

    api.setScrollLocked = (locked) => {
        try {
            if (locked) {
                // В бою свайпы мешают стикам.
                tg.disableVerticalSwipes?.();
            } else {
                // В меню и на паузе свайпы нужны, чтобы листать контент.
                tg.enableVerticalSwipes?.();
            }
        } catch (error) {
            // Старые версии Telegram игнорируют метод.
        }
    };

    api.setBackButton = (visible) => {
        const backButton = tg.BackButton;

        if (!backButton) {
            return;
        }

        try {
            if (visible) {
                if (!backButtonBound) {
                    backButton.onClick(onBackButton);
                    backButtonBound = true;
                }

                backButton.show();
            } else {
                if (backButtonBound) {
                    backButton.offClick(onBackButton);
                    backButtonBound = false;
                }

                backButton.hide();
            }
        } catch (error) {
            // Кнопка «назад» не критична для игры.
        }
    };

    api.haptic = (type) => {
        const feedback = tg.HapticFeedback;
        const method = feedback?.[type];

        if (typeof method === "function") {
            try {
                method.call(feedback);
            } catch (error) {
                // Тактильная отдача — необязательный бонус.
            }
        }
    };

    try {
        tg.ready();
        tg.expand();
        // Не блокируем вертикальные свайпы в меню: иначе не листается экран
        // выбора аппарата и не видны кнопки «Начать бой».
        tg.enableVerticalSwipes?.();
        tg.requestFullscreen?.();
    } catch (error) {
        // Mini App продолжает работать и без fullscreen.
    }

    ensureRotateOverlay();
    updateOrientation();
    applyViewport();
    applyTheme();

    window.addEventListener("resize", () => {
        applyViewport();
        updateOrientation();
    });
    window.addEventListener("orientationchange", () => {
        applyViewport();
        updateOrientation();
    });

    tg.onEvent?.("viewportChanged", () => {
        applyViewport();
        updateOrientation();
    });
    tg.onEvent?.("safeAreaChanged", applyViewport);
    tg.onEvent?.("contentSafeAreaChanged", applyViewport);
    tg.onEvent?.("fullscreenChanged", () => {
        applyViewport();
        lockLandscape();
    });
    tg.onEvent?.("themeChanged", applyTheme);
})();
