// Remembers the chosen UI language in this browser. First-time visitors get their browser's language.
window.krakowLanguage = {
    get: function () {
        try {
            var saved = localStorage.getItem('krakow-lang');
            if (saved) return saved;
        } catch (e) { /* storage blocked: fall back to the browser language */ }
        return (navigator.language || 'en').toLowerCase();
    },
    set: function (code) {
        try { localStorage.setItem('krakow-lang', code); } catch (e) { /* ignore */ }
        document.documentElement.lang = code;
    }
};

// Light/dark theme. The default (dark) is applied before first paint by an inline script in App.razor; this keeps it in sync afterwards.
window.krakowTheme = {
    get: function () {
        return document.documentElement.getAttribute('data-bs-theme') === 'light' ? 'light' : 'dark';
    },
    set: function (theme) {
        theme = theme === 'light' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-bs-theme', theme);
        try { localStorage.setItem('krakow-theme', theme); } catch (e) { /* ignore */ }
        return theme;
    }
};
