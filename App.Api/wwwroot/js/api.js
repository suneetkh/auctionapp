// Shared fetch + auth-header helper for all pages.
const Api = (() => {
    function token() { return localStorage.getItem('token'); }
    function setSession(data) {
        localStorage.setItem('token', data.token);
        localStorage.setItem('email', data.email);
        localStorage.setItem('role', data.role);
        localStorage.setItem('displayName', data.displayName);
        localStorage.setItem('userId', data.userId);
    }
    function clearSession() { localStorage.clear(); }
    function currentRole() { return localStorage.getItem('role'); }
    function currentUserId() { return parseInt(localStorage.getItem('userId') || '0', 10); }
    function isLoggedIn() { return !!token(); }

    async function request(method, url, body, isFormData) {
        const headers = {};
        if (!isFormData) headers['Content-Type'] = 'application/json';
        if (token()) headers['Authorization'] = 'Bearer ' + token();

        const opts = { method, headers };
        if (body !== undefined && body !== null) {
            opts.body = isFormData ? body : JSON.stringify(body);
        }
        const res = await fetch(url, opts);
        if (res.status === 401) {
            clearSession();
            if (!location.pathname.endsWith('login.html') && !location.pathname.endsWith('display.html')) {
                location.href = 'login.html';
            }
            throw new Error('Unauthorized');
        }
        const contentType = res.headers.get('content-type') || '';
        let data = null;
        if (contentType.includes('application/json')) {
            data = await res.json().catch(() => null);
        } else if (contentType.includes('text/csv')) {
            data = await res.blob();
        }
        if (!res.ok) {
            const err = new Error((data && data.error) || `Request failed (${res.status})`);
            err.status = res.status;
            err.data = data;
            throw err;
        }
        return data;
    }

    return {
        get: (url) => request('GET', url),
        post: (url, body) => request('POST', url, body ?? {}),
        put: (url, body) => request('PUT', url, body ?? {}),
        patch: (url, body) => request('PATCH', url, body ?? {}),
        del: (url) => request('DELETE', url),
        postForm: (url, formData) => request('POST', url, formData, true),
        setSession, clearSession, isLoggedIn, currentRole, currentUserId, token
    };
})();

function requireAuth(allowedRoles) {
    if (!Api.isLoggedIn()) { location.href = 'login.html'; return false; }
    if (allowedRoles && !allowedRoles.includes(Api.currentRole())) {
        alert('You do not have permission to view this page.');
        location.href = 'login.html';
        return false;
    }
    return true;
}

function fmtMoney(n) {
    if (n === null || n === undefined) return '-';
    return Number(n).toLocaleString(undefined, { maximumFractionDigits: 0 });
}

function togglePasswordVisibility(inputId, button) {
    const input = document.getElementById(inputId);
    if (!input) return;
    const showing = input.type === 'text';
    input.type = showing ? 'password' : 'text';
    button.textContent = showing ? 'Show' : 'Hide';
    button.setAttribute('aria-label', showing ? 'Show password' : 'Hide password');
}

// Shared across the live console, public display, and tournament draw so each
// configured team keeps the same visual identity throughout the app.
const TEAM_COLORS = ['#06b6d4', '#2563eb', '#10b981', '#f59e0b', '#ec4899', '#14b8a6', '#ef4444', '#eab308'];
function teamColor(index) {
    return TEAM_COLORS[Math.abs(Number(index) || 0) % TEAM_COLORS.length];
}

// Turns enum-style status strings like "UnsoldPoolOpen" into "Unsold Pool Open" for display.
// Use this wherever a status is shown as text; keep the raw value for statusBadgeClass/API calls.
function humanizeStatus(status) {
    if (!status) return '';
    return status.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function statusBadgeClass(status) {
    switch (status) {
        case 'Sold': return 'badge-sold';
        case 'Unsold': case 'FinalUnsold': return 'badge-unsold';
        case 'Selected': return 'bg-info text-dark';
        case 'Bidding': return 'badge-live';
        case 'Completed': case 'Archived': case 'Withdrawn': return 'badge-locked';
        default: return 'badge-default';
    }
}

// Multiple SignalR events can describe one action (for example, sold + balance updated).
// Share one in-flight refresh instead of downloading the same state several times at once.
function singleFlight(task) {
    let active = null;
    return (...args) => {
        if (active) return active;
        active = Promise.resolve().then(() => task(...args)).finally(() => { active = null; });
        return active;
    };
}
