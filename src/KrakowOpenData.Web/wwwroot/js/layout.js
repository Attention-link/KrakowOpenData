// SB Admin sidebar toggle. Delegated, so it keeps working after Blazor re-renders the layout.
document.addEventListener('click', function (event) {
    if (event.target.closest && event.target.closest('#sidebarToggle')) {
        event.preventDefault();
        document.body.classList.toggle('sb-sidenav-toggled');
    }
});

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
new MutationObserver(function () { labelTableCells(document); }).observe(document.documentElement, { childList: true, subtree: true });
document.addEventListener('DOMContentLoaded', function () { labelTableCells(document); });
