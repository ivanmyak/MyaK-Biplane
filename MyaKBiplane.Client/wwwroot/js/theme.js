(function () {
    "use strict";

    const storageKey = "myak-theme";
    const themes = ["dark", "light"];
    const themeColors = {
        dark: "#07111f",
        light: "#16b9ec"
    };

    function normalize(value) {
        return themes.includes(value) ? value : null;
    }

    function systemTheme() {
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: light)").matches
            ? "light"
            : "dark";
    }

    function storedTheme() {
        try {
            return normalize(window.localStorage.getItem(storageKey));
        } catch (error) {
            // Приватный режим или заблокированное хранилище — работаем без сохранения.
            return null;
        }
    }

    function apply(theme) {
        const value = normalize(theme) || systemTheme();
        document.documentElement.dataset.theme = value;

        const meta = document.querySelector('meta[name="theme-color"]');
        if (meta) {
            meta.setAttribute("content", themeColors[value]);
        }

        return value;
    }

    function current() {
        return normalize(document.documentElement.dataset.theme) || systemTheme();
    }

    function set(theme) {
        const value = apply(theme);

        try {
            window.localStorage.setItem(storageKey, value);
        } catch (error) {
            // Настройка просто не сохранится между визитами.
        }

        return value;
    }

    function toggle() {
        return set(current() === "light" ? "dark" : "light");
    }

    // Применяем тему до первой отрисовки, чтобы не было вспышки чужой палитры.
    apply(storedTheme());

    if (window.matchMedia) {
        const media = window.matchMedia("(prefers-color-scheme: light)");
        const onChange = (event) => {
            // Системную тему слушаем только пока пользователь не выбрал свою.
            if (!storedTheme()) {
                apply(event.matches ? "light" : "dark");
            }
        };

        if (media.addEventListener) {
            media.addEventListener("change", onChange);
        } else if (media.addListener) {
            media.addListener(onChange);
        }
    }

    window.myakTheme = {
        current,
        set,
        toggle,
        system: systemTheme
    };
})();
