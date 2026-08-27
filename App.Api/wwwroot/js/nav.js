// Renders a shared top navbar into #app-navbar based on current role.
function renderNavbar(active) {
    const el = document.getElementById('app-navbar');
    if (!el) return;
    const role = Api.currentRole();
    const name = localStorage.getItem('displayName') || '';

    // Teams/Players/Live Auction/Reports are all per-auction pages with no meaningful
    // "generic" destination, so they're reached from each auction's row on the dashboard
    // instead of living in the top nav. For admins, the Player Auction brand already routes
    // through index.html to the dashboard, so a second Dashboard link is redundant.
    const links = [];
    if (role === 'TeamOwner') {
        links.push(['team-dashboard.html', 'My Team', 'team-dashboard.html']);
    }
    if (role === 'SuperAdmin') {
        links.push(['users.html', 'Users & Access', 'users.html']);
    }
    if (role) {
        links.push(['account.html', 'Account', 'account.html']);
    }

    const navLinks = links.map(([matchHref, label, href]) =>
        `<a class="nav-link ${active === matchHref ? 'active fw-bold' : ''}" href="${href}">${label}</a>`
    ).join('');

    el.innerHTML = `
    <nav class="navbar navbar-expand-lg navbar-dark bg-dark mb-4">
      <div class="container-fluid">
        <a class="navbar-brand" href="index.html">🏆 Player Auction</a>
        <button class="navbar-toggler" type="button" data-bs-toggle="collapse" data-bs-target="#navContent">
          <span class="navbar-toggler-icon"></span>
        </button>
        <div class="collapse navbar-collapse" id="navContent">
          <div class="navbar-nav me-auto">${navLinks}</div>
          <div class="d-flex align-items-center text-light">
            <span class="me-3 small">${name} (${role || ''})</span>
            <button class="btn btn-sm btn-outline-light" onclick="logout()">Logout</button>
          </div>
        </div>
      </div>
    </nav>`;
}

function logout() {
    Api.clearSession();
    location.href = 'login.html';
}
