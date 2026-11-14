// Light / dark / system theme choice for the dashboard. Stores the choice where the library's
// theme-init.js reads it before first paint (localStorage "bb-theme", isDarkMode); "system" removes
// the saved entry so theme-init.js and the library fall back to prefers-color-scheme.
window.peakTheme = {
    key: "bb-theme",
    get: function () {
        try {
            var raw = localStorage.getItem(this.key);
            if (!raw) { return "system"; }
            return JSON.parse(raw).isDarkMode ? "dark" : "light";
        } catch (e) {
            return "system";
        }
    },
    set: function (mode) {
        var dark;
        if (mode === "system") {
            localStorage.removeItem(this.key);
            dark = window.matchMedia("(prefers-color-scheme: dark)").matches;
        } else {
            var entry = {};
            try { entry = JSON.parse(localStorage.getItem(this.key)) || {}; } catch (e) { entry = {}; }
            dark = mode === "dark";
            entry.isDarkMode = dark;
            localStorage.setItem(this.key, JSON.stringify(entry));
        }
        document.documentElement.classList.toggle("dark", dark);
    }
};
