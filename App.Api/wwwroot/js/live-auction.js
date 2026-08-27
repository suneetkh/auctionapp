// Live auction console — admin controls, wired to SignalR with REST reconciliation.
if (!requireAuth(['SuperAdmin', 'AuctionAdmin'])) { throw new Error('unauth'); }
if (typeof renderNav === 'function') renderNav('dashboard');

const params = new URLSearchParams(location.search);
const auctionId = params.get('id') || params.get('auctionId');
if (!auctionId) { alert('Missing auction id'); location.href = 'admin-dashboard.html'; }

let auction = null;
let teams = [];
let currentPlayer = null;

async function refreshState() {
    const state = await Api.get(`/api/auctions/${auctionId}/state`);
    auction = state.auction;
    currentPlayer = state.currentPlayer;
    teams = await Api.get(`/api/auctions/${auctionId}/teams`);

    document.getElementById('auctionName').textContent = auction.name;
    document.getElementById('statusBadge').textContent = auction.status;
    document.getElementById('statusBadge').className = `badge ${statusBadgeClass(auction.status)}`;
    document.getElementById('unsoldCount').textContent = state.unsoldCount;

    renderTeamSelects();
    renderTeamBalances();
    renderPlayer(currentPlayer);
    renderBidHistory(state.recentBids || []);
    renderRecentSold(state.recentSales || []);
}

function renderTeamSelects() {
    const options = teams.map(t => `<option value="${t.id}">${t.name} (${fmtMoney(t.availableBalance)})</option>`).join('');
    document.getElementById('teamSelect').innerHTML = options;
    document.getElementById('sellTeamSelect').innerHTML = options;
}

function renderTeamBalances() {
    document.getElementById('teamBalances').innerHTML = teams.map(t => `
        <div class="d-flex justify-content-between border-bottom py-1">
            <span>${t.name}</span>
            <span class="${t.availableBalance < t.openingBalance * 0.15 ? 'balance-low' : 'balance-positive'}">${fmtMoney(t.availableBalance)}</span>
        </div>`).join('');
}

function renderPlayer(player) {
    const noPlayer = document.getElementById('noPlayer');
    const info = document.getElementById('playerInfo');
    const card = document.getElementById('playerCard');
    if (!player) {
        noPlayer.classList.remove('d-none');
        info.classList.add('d-none');
        card.classList.remove('bidding-active');
        return;
    }
    noPlayer.classList.add('d-none');
    info.classList.remove('d-none');
    card.classList.toggle('bidding-active', player.status === 'Bidding' || player.status === 'Selected');

    document.getElementById('playerName').textContent = player.name;
    document.getElementById('playerRole').textContent = player.role;
    document.getElementById('playerTags').textContent = player.skillTags || '';
    document.getElementById('playerBase').textContent = fmtMoney(player.basePrice);
    if (player.photoUrl) {
        document.getElementById('playerPhoto').src = player.photoUrl;
        document.getElementById('playerPhoto').classList.remove('d-none');
    }
}

function renderBidHistory(bids) {
    document.getElementById('bidHistory').innerHTML = bids.map(b => `
        <div class="timeline-item ${b.isValid ? '' : 'text-danger'}">
            <strong>${fmtMoney(b.amount)}</strong> by Team #${b.teamId} ${b.isValid ? '' : `(rejected: ${b.invalidReason})`}
            <div class="text-muted small">${new Date(b.createdAt).toLocaleTimeString()}</div>
        </div>`).join('') || '<div class="text-muted">No bids yet</div>';
}

function renderRecentSold(sales) {
    document.getElementById('recentSold').innerHTML = sales.map(s => `
        <div class="d-flex justify-content-between border-bottom py-1">
            <span>Player #${s.playerId} → Team #${s.teamId}</span>
            <span class="text-success fw-bold">${fmtMoney(s.finalAmount)}</span>
        </div>`).join('') || '<div class="text-muted">No sales yet</div>';
}

async function spin() {
    const card = document.getElementById('playerCard');
    card.classList.add('wheel-spin');
    setTimeout(() => card.classList.remove('wheel-spin'), 1200);
    try {
        await Api.post(`/api/auctions/${auctionId}/spin`);
    } catch (err) { alert(err.message); }
}

async function pauseResume(action) {
    try { await Api.post(`/api/auctions/${auctionId}/${action}`); } catch (err) { alert(err.message); }
}

async function openUnsoldPool() {
    try { await Api.post(`/api/auctions/${auctionId}/open-unsold-pool`); refreshState(); } catch (err) { alert(err.message); }
}

async function nextRound() {
    try { await Api.post(`/api/auctions/${auctionId}/next-round`); refreshState(); } catch (err) { alert(err.message); }
}

let bidInFlight = false;
async function placeBid() {
    if (bidInFlight) return; // idempotency guard against double-click
    if (!currentPlayer) { alert('No player selected'); return; }
    const teamId = document.getElementById('teamSelect').value;
    const amount = parseFloat(document.getElementById('bidAmount').value);
    if (!teamId || !amount) { alert('Select a team and amount'); return; }
    bidInFlight = true;
    try {
        await Api.post(`/api/auctions/${auctionId}/bids`, { playerId: currentPlayer.id, teamId: parseInt(teamId), amount });
        document.getElementById('bidAmount').value = '';
    } catch (err) { alert(err.message); }
    finally { bidInFlight = false; }
}

async function quickBid() {
    if (!currentPlayer) { alert('No player selected'); return; }
    const teamId = document.getElementById('teamSelect').value;
    if (!teamId) { alert('Select a team'); return; }
    const increment = auction.bidIncrementAmount;
    const highest = (await Api.get(`/api/auctions/${auctionId}/reports/bids`))
        .filter(b => b.playerId === currentPlayer.id && b.isValid)
        .sort((a,b) => b.amount - a.amount)[0];
    const base = highest ? highest.amount : Math.max(currentPlayer.basePrice, auction.minimumBidAmount);
    const amount = highest ? highest.amount + increment : base;
    document.getElementById('bidAmount').value = amount;
    await placeBid();
}

let sellInFlight = false;
async function sellPlayer() {
    if (sellInFlight) return; // idempotency guard against double-click
    if (!currentPlayer) { alert('No player selected'); return; }
    const teamId = document.getElementById('sellTeamSelect').value;
    const amount = parseFloat(document.getElementById('bidAmount').value) || currentPlayer.basePrice;
    if (!teamId) { alert('Select a winning team'); return; }
    sellInFlight = true;
    try {
        await Api.post(`/api/auctions/${auctionId}/sell`, { playerId: currentPlayer.id, teamId: parseInt(teamId), amount });
        refreshState();
    } catch (err) { alert(err.message); }
    finally { sellInFlight = false; }
}

async function markUnsold() {
    if (!currentPlayer) { alert('No player selected'); return; }
    try {
        await Api.post(`/api/auctions/${auctionId}/mark-unsold`, { playerId: currentPlayer.id });
        refreshState();
    } catch (err) { alert(err.message); }
}

// SignalR wiring: on any push event, just re-fetch full state via REST
// rather than trusting the push payload directly for critical UI state.
const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/auction', { accessTokenFactory: () => Api.token() || '' })
    .withAutomaticReconnect()
    .build();

['player_selected','bid_placed','bid_rejected','player_sold','player_unsold','balance_updated',
 'unsold_pool_opened','auction_status_changed','auction_completed','correction_applied']
 .forEach(evt => connection.on(evt, () => refreshState().catch(console.error)));

connection.start()
    .then(() => connection.invoke('JoinAuction', parseInt(auctionId)))
    .then(refreshState)
    .catch(console.error);

connection.onreconnected(() => {
    connection.invoke('JoinAuction', parseInt(auctionId)).then(refreshState).catch(console.error);
});

refreshState().catch(err => console.error(err));
