// SB Admin sidebar toggle. Delegated, so it keeps working after Blazor re-renders the layout.
document.addEventListener('click', function (event) {
    if (event.target.closest && event.target.closest('#sidebarToggle')) {
        event.preventDefault();
        document.body.classList.toggle('sb-sidenav-toggled');
    }
});
