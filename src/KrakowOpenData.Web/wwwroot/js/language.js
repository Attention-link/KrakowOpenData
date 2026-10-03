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
