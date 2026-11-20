(function () {
    "use strict";

    const root = document.documentElement;
    const tg = window.Telegram?.WebApp;

    // SDK подключается и вне Telegram, но initData и платформа остаются пустыми.
    // Поэтому «настоящим» Mini App считаем только контекст с данными Telegram.
    const inTelegram = !!tg
        && (!!tg.initData
            || !!tg.initDataUnsafe?.user
            || (!!tg.platform && tg.platform !== "unknown"));

    const api = {
        isAvailable: () => inTelegram,
        isMobile: () => inTelegram && (tg.isMobile === true
            || ["android", "ios"].includes((tg.platform || "").toLowerCase())),
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
        setBackButton: () => {},
        haptic: () => {}
    };

    window.myakTelegram = api;

    if (!inTelegram) {
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

    let backButtonBound = false;

    function onBackButton() {
        window.location.assign(menuUrl());
    }

    api.setGameActive = (active) => {
        try {
            if (active) {
                tg.enableClosingConfirmation?.();
                tg.requestFullscreen?.();
            } else {
                tg.disableClosingConfirmation?.();
            }
        } catch (error) {
            // Старые версии Telegram не поддерживают часть методов.
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
        tg.disableVerticalSwipes?.();
        tg.requestFullscreen?.();
    } catch (error) {
        // Mini App продолжает работать и без fullscreen.
    }

    applyViewport();
    applyTheme();

    tg.onEvent?.("viewportChanged", applyViewport);
    tg.onEvent?.("safeAreaChanged", applyViewport);
    tg.onEvent?.("contentSafeAreaChanged", applyViewport);
    tg.onEvent?.("fullscreenChanged", applyViewport);
    tg.onEvent?.("themeChanged", applyTheme);
})();
