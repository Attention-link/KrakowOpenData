// SB Admin sidebar toggle. Delegated, so it keeps working after Blazor re-renders the layout.
// The menu is open by default from 992px up and closed below; the toggle class flips that. aria-expanded follows.
function menuOpen() {
    var toggled = document.body.classList.contains('sb-sidenav-toggled');
    return window.matchMedia('(min-width: 992px)').matches ? !toggled : toggled;
}
function syncMenuToggle() {
    var btn = document.getElementById('sidebarToggle');
    if (btn && btn.getAttribute('aria-expanded') !== String(menuOpen())) btn.setAttribute('aria-expanded', String(menuOpen()));
}
document.addEventListener('click', function (event) {
    if (event.target.closest && event.target.closest('#sidebarToggle')) {
        event.preventDefault();
        document.body.classList.toggle('sb-sidenav-toggled');
        syncMenuToggle();
    }
});
window.matchMedia('(min-width: 992px)').addEventListener('change', syncMenuToggle);

// Skip link: "#main-content" would be resolved against <base href="/"> and send Blazor to the home page, so move focus here.
// Capture phase, so it runs before Blazor's own link handling.
document.addEventListener('click', function (event) {
    var link = event.target.closest && event.target.closest('a.skip-link');
    var main = document.getElementById('main-content');
    if (!link || !main) return;
    event.preventDefault();
    event.stopPropagation();
    main.focus();
}, true);

// Tables turn into stacked cards on phones (see app.css); each cell shows its column title via data-label.
function labelTableCells(root) {
    (root || document).querySelectorAll('table.table').forEach(function (table) {
        var heads = Array.prototype.map.call(table.querySelectorAll('thead th'), function (th) { return th.textContent.trim(); });
        table.querySelectorAll('tbody tr').forEach(function (row) {
            Array.prototype.forEach.call(row.children, function (cell, i) {
                if (heads[i] && cell.getAttribute('data-label') !== heads[i]) cell.setAttribute('data-label', heads[i]);
            });
        });
    });
}
new MutationObserver(function () { labelTableCells(document); syncMenuToggle(); }).observe(document.documentElement, { childList: true, subtree: true });
document.addEventListener('DOMContentLoaded', function () { labelTableCells(document); syncMenuToggle(); });
