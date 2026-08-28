const TournamentDraw = (() => {
    const TAU = Math.PI * 2;
    let auctionId = null;
    let auctionName = 'Tournament Draw';
    let names = [];
    let teamColors = [];
    let remaining = [];
    let assignments = [];
    let rotation = 0;
    let spinning = false;
    let locked = false;
    let pendingSave = Promise.resolve();
    let canvas;
    let ctx;

    const el = id => document.getElementById(id);
    const storageKey = () => `tournamentDraw:${auctionId}`;

    function teamLabel(index) {
        let value = index + 1;
        let letters = '';
        while (value > 0) {
            value--;
            letters = String.fromCharCode(65 + (value % 26)) + letters;
            value = Math.floor(value / 26);
        }
        return `Team ${letters}`;
    }

    function escapeHtml(value) {
        return String(value)
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    async function loadSavedState() {
        try {
            let saved = null;
            try { saved = await Api.get(`/api/auctions/${auctionId}/planning/draw`); }
            catch (error) { console.error('Unable to load saved tournament draw', error); }
            if (!saved) {
                saved = JSON.parse(localStorage.getItem(storageKey()) || 'null');
                if (saved) Api.put(`/api/auctions/${auctionId}/planning/draw`, { state: saved }).catch(console.error);
            }
            if (!saved) return false;
            names = Array.isArray(saved.names) ? saved.names : [];
            teamColors = Array.isArray(saved.teamColors) ? saved.teamColors : [];
            remaining = Array.isArray(saved.remaining) ? saved.remaining : [];
            assignments = Array.isArray(saved.assignments) ? saved.assignments : [];
            return names.length > 0;
        } catch (error) {
            console.error('Unable to restore tournament draw', error);
            return false;
        }
    }

    function saveState() {
        const saved = { names, teamColors, remaining, assignments };
        localStorage.setItem(storageKey(), JSON.stringify(saved));
        pendingSave = pendingSave.catch(() => {}).then(() => Api.put(`/api/auctions/${auctionId}/planning/draw`, { state: saved }));
        pendingSave.catch(error => console.error('Unable to save tournament draw', error));
        window.dispatchEvent(new CustomEvent('tournament-draw-updated', {
            detail: { auctionId: String(auctionId), assignments: assignments.map(item => ({ ...item })) }
        }));
    }

    function generateInputs(count, values = names) {
        if (!Number.isInteger(count) || count < 0) {
            alert('Unable to read the auction team count.');
            return;
        }

        el('drawSetupCount').textContent = `${count} team${count === 1 ? '' : 's'}`;
        if (count === 0) {
            return;
        }
    }

    async function syncAuctionTeams() {
        if (spinning || locked) return;
        try {
            const auctionTeams = await Api.get(`/api/auctions/${auctionId}/teams`);
            names = auctionTeams.map(team => team.name);
            teamColors = auctionTeams.map((team, index) => team.teamColor || teamColor(index));
            remaining = [...names];
            assignments = [];
            rotation = 0;
            generateInputs(auctionTeams.length, names);
            saveState();
            renderAll();
            showLatestResult(null);
        } catch (err) {
            alert(`Could not refresh auction teams: ${err.message}`);
        }
    }

    function newDraw() {
        if (locked) return;
        if (!names.length) { alert('Add teams to this auction before starting a draw.'); return; }

        remaining = [...names];
        assignments = [];
        rotation = 0;
        saveState();
        renderAll();
        showLatestResult(null);
    }

    // Rejection sampling avoids modulo bias, so every remaining wheel segment has exactly
    // the same probability even when its count does not divide the uint32 range evenly.
    function cryptoRandomIndex(length) {
        if (!Number.isInteger(length) || length < 1) throw new Error('No names remain to draw');
        const range = 0x100000000;
        const limit = Math.floor(range / length) * length;
        const value = new Uint32Array(1);
        do { crypto.getRandomValues(value); } while (value[0] >= limit);
        return value[0] % length;
    }

    function easeOutQuint(t) {
        return 1 - Math.pow(1 - t, 5);
    }

    function spin() {
        if (locked || spinning || !remaining.length || assignments.length >= names.length) return;

        spinning = true;
        updateControls();
        showLatestResult(null);
        el('drawStatus').textContent = `Spinning for ${teamLabel(assignments.length)}...`;

        const selectedIndex = cryptoRandomIndex(remaining.length);
        const slice = TAU / remaining.length;
        const desired = -Math.PI / 2 - (selectedIndex + .5) * slice;
        const currentNormalized = ((rotation % TAU) + TAU) % TAU;
        const desiredNormalized = ((desired % TAU) + TAU) % TAU;
        const forwardToTarget = (desiredNormalized - currentNormalized + TAU) % TAU;
        const startRotation = rotation;
        const endRotation = rotation + (TAU * 7) + forwardToTarget;
        const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        const duration = reduceMotion ? 250 : 4300;
        const startedAt = performance.now();

        function frame(now) {
            const progress = Math.min(1, (now - startedAt) / duration);
            rotation = startRotation + (endRotation - startRotation) * easeOutQuint(progress);
            drawWheel();
            if (progress < 1) {
                requestAnimationFrame(frame);
                return;
            }

            const winner = remaining[selectedIndex];
            const slot = teamLabel(assignments.length);
            assignments.push({ slot, name: winner });
            remaining.splice(selectedIndex, 1);
            spinning = false;
            saveState();
            renderAll();
            showLatestResult({ slot, name: winner });
        }

        requestAnimationFrame(frame);
    }

    function showLatestResult(result) {
        const box = el('drawLatestResult');
        if (!result) {
            box.classList.add('d-none');
            box.textContent = '';
            return;
        }
        box.innerHTML = `<strong>${escapeHtml(result.slot)}</strong> is assigned to <strong>${escapeHtml(result.name)}</strong>`;
        box.classList.remove('d-none');
    }

    function renderStatus() {
        const finished = names.length > 0 && assignments.length === names.length;
        const current = teamLabel(assignments.length);
        el('drawCurrentTeam').textContent = finished ? 'Draw Complete' : current;
        el('drawStatus').textContent = !names.length
            ? 'Enter names and load the wheel to begin.'
            : finished
                ? 'Every team slot has been assigned.'
                : `${remaining.length} name${remaining.length === 1 ? '' : 's'} remaining on the wheel`;
        el('drawProgress').textContent = `${assignments.length} / ${names.length} drawn`;
    }

    function renderResults() {
        if (!names.length) {
            el('drawResultsBody').innerHTML = '<tr><td colspan="4" class="text-center text-muted py-4">No results yet</td></tr>';
            return;
        }

        el('drawResultsBody').innerHTML = names.map((_, index) => {
            const result = assignments[index];
            return `<tr>
                <td class="text-muted">${index + 1}</td>
                <td class="fw-bold">${teamLabel(index)}</td>
                <td>${result ? `<strong>${escapeHtml(result.name)}</strong>` : '<span class="text-muted">Waiting for draw</span>'}</td>
                <td>${result ? '<span class="badge bg-success">Assigned</span>' : '<span class="badge bg-light text-dark border">Pending</span>'}</td>
            </tr>`;
        }).join('');
    }

    function updateControls() {
        const complete = names.length > 0 && assignments.length >= names.length;
        el('drawSpinBtn').disabled = locked || spinning || !remaining.length || complete;
        el('drawSpinBtn').textContent = locked ? 'Assignments Locked' : spinning ? 'Spinning...' : complete ? 'Draw Complete' : `Spin for ${teamLabel(assignments.length)}`;
        el('drawNewBtn').disabled = locked || spinning;
        el('drawGenerateBtn').disabled = locked || spinning;
        const lockButton = el('drawLockBtn');
        lockButton.disabled = locked || spinning || !complete;
        lockButton.textContent = locked ? 'Team Assignments Locked' : 'Lock Team Assignments';
    }

    async function lockAssignments() {
        if (locked || spinning || !names.length || assignments.length !== names.length) return;
        try {
            await pendingSave;
            await Api.post(`/api/auctions/${auctionId}/planning/draw/lock`);
            setLocked(true);
            window.notify?.('Team assignments locked. Unlock them from Auction Lifecycle.', 'success');
            window.dispatchEvent(new CustomEvent('planning-locks-updated'));
        } catch (error) {
            window.notify?.(error.message, 'error');
        }
    }

    function setLocked(value) {
        locked = Boolean(value);
        updateControls();
    }

    function resizeCanvas() {
        const wrap = canvas.parentElement;
        const cssSize = Math.max(280, Math.floor(wrap.getBoundingClientRect().width || 520));
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        canvas.width = Math.floor(cssSize * dpr);
        canvas.height = Math.floor(cssSize * dpr);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        canvas.dataset.cssSize = cssSize;
        drawWheel();
    }

    function fitText(text, maxWidth, initialSize, weight = 700) {
        let size = initialSize;
        do {
            ctx.font = `${weight} ${size}px system-ui, sans-serif`;
            if (ctx.measureText(text).width <= maxWidth) return size;
            size--;
        } while (size > 8);
        return size;
    }

    function drawCenter(cx, cy, radius) {
        ctx.save();
        ctx.beginPath();
        ctx.arc(cx, cy, radius, 0, TAU);
        ctx.fillStyle = '#ffffff';
        ctx.shadowColor = 'rgba(28, 15, 48, .3)';
        ctx.shadowBlur = 18;
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.lineWidth = 5;
        ctx.strokeStyle = '#6f42c1';
        ctx.stroke();

        const displayName = auctionName || 'Tournament Draw';
        const words = displayName.split(/\s+/);
        const lines = [];
        let line = '';
        words.forEach(word => {
            const candidate = line ? `${line} ${word}` : word;
            if (candidate.length > 18 && line && lines.length === 0) {
                lines.push(line);
                line = word;
            } else {
                line = candidate;
            }
        });
        if (line) lines.push(line);
        const visibleLines = lines.slice(0, 2);
        if (lines.length > 2) visibleLines[1] += '…';
        const fontSize = fitText(visibleLines.reduce((a, b) => a.length > b.length ? a : b, ''), radius * 1.55, Math.max(14, radius * .24), 800);
        ctx.font = `800 ${fontSize}px system-ui, sans-serif`;
        ctx.fillStyle = '#35205f';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        const lineHeight = fontSize * 1.12;
        visibleLines.forEach((text, index) => {
            ctx.fillText(text, cx, cy + (index - (visibleLines.length - 1) / 2) * lineHeight, radius * 1.55);
        });
        ctx.restore();
    }

    function drawWheel() {
        if (!ctx || !canvas) return;
        const size = Number(canvas.dataset.cssSize || 520);
        ctx.clearRect(0, 0, size, size);
        const cx = size / 2;
        const cy = size / 2;
        const radius = size * .455;

        if (!remaining.length) {
            ctx.beginPath();
            ctx.arc(cx, cy, radius, 0, TAU);
            ctx.fillStyle = '#e9e4f3';
            ctx.fill();
            ctx.lineWidth = 8;
            ctx.strokeStyle = '#6f42c1';
            ctx.stroke();
            drawCenter(cx, cy, radius * .29);
            return;
        }

        const slice = TAU / remaining.length;
        remaining.forEach((name, index) => {
            const start = rotation + index * slice;
            const end = start + slice;
            const originalTeamIndex = names.indexOf(name);
            ctx.beginPath();
            ctx.moveTo(cx, cy);
            ctx.arc(cx, cy, radius, start, end);
            ctx.closePath();
            const segmentColor = teamColors[originalTeamIndex] || teamColor(originalTeamIndex >= 0 ? originalTeamIndex : index);
            ctx.fillStyle = segmentColor;
            ctx.fill();
            ctx.lineWidth = Math.max(1, size * .003);
            ctx.strokeStyle = 'rgba(255,255,255,.78)';
            ctx.stroke();

            const mid = start + slice / 2;
            const normalized = ((mid % TAU) + TAU) % TAU;
            const flipText = normalized > Math.PI / 2 && normalized < Math.PI * 1.5;
            const maxLabelWidth = radius * .56;
            const initialFontSize = Math.max(10, Math.min(18, 170 / Math.max(remaining.length, 1) + 8));
            ctx.save();
            ctx.translate(cx, cy);
            ctx.rotate(mid);
            if (flipText) ctx.rotate(Math.PI);
            const labelX = flipText ? -radius * .62 : radius * .62;
            fitText(name, maxLabelWidth, initialFontSize, 700);
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillStyle = readableTextColor(segmentColor);
            ctx.shadowColor = 'rgba(0,0,0,.45)';
            ctx.shadowBlur = 3;
            ctx.fillText(name, labelX, 0, maxLabelWidth);
            ctx.restore();
        });

        ctx.beginPath();
        ctx.arc(cx, cy, radius, 0, TAU);
        ctx.lineWidth = Math.max(5, size * .012);
        ctx.strokeStyle = '#3f2866';
        ctx.stroke();
        drawCenter(cx, cy, radius * .29);
    }

    const textColorCache = new Map();
    function readableTextColor(color) {
        if (textColorCache.has(color)) return textColorCache.get(color);
        const sample = document.createElement('canvas');
        sample.width = sample.height = 1;
        const sampleContext = sample.getContext('2d');
        sampleContext.fillStyle = color;
        sampleContext.fillRect(0, 0, 1, 1);
        const [r, g, b] = sampleContext.getImageData(0, 0, 1, 1).data;
        const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
        const result = luminance > .62 ? '#172033' : '#ffffff';
        textColorCache.set(color, result);
        return result;
    }

    function renderAll() {
        renderStatus();
        renderResults();
        updateControls();
        drawWheel();
    }

    async function init(id) {
        auctionId = id;
        canvas = el('tournamentWheel');
        ctx = canvas.getContext('2d');

        try {
            const locks = await Api.get(`/api/auctions/${auctionId}/planning/locks`);
            locked = locks.drawLocked === true;
        } catch (error) { console.error('Unable to load planning locks', error); }
        const restored = await loadSavedState();
        let auctionTeams = [];
        try {
            auctionTeams = await Api.get(`/api/auctions/${auctionId}/teams`);
        } catch (err) {
            console.error('Unable to load auction teams for draw', err);
        }
        const configuredNames = auctionTeams.map(team => team.name);
        if (auctionTeams.length) teamColors = auctionTeams.map((team, index) => team.teamColor || teamColor(index));
        const canRestore = restored && names.length === configuredNames.length &&
            names.every((name, index) => name === configuredNames[index]);
        if (!canRestore && !locked) {
            names = configuredNames;
            remaining = [...configuredNames];
            assignments = [];
            saveState();
        }
        generateInputs(auctionTeams.length, names);
        el('drawManageTeamsLink').href = `teams.html?auctionId=${auctionId}`;

        el('drawGenerateBtn').addEventListener('click', syncAuctionTeams);
        el('drawSpinBtn').addEventListener('click', spin);
        el('drawNewBtn').addEventListener('click', newDraw);
        el('drawLockBtn').addEventListener('click', lockAssignments);
        document.querySelector('a[href="#tab-tournament-draw"]').addEventListener('shown.bs.tab', resizeCanvas);

        new ResizeObserver(resizeCanvas).observe(canvas.parentElement);
        resizeCanvas();
        renderAll();
        if (assignments.length) showLatestResult(assignments[assignments.length - 1]);
        window.dispatchEvent(new CustomEvent('tournament-draw-updated', {
            detail: { auctionId: String(auctionId), assignments: assignments.map(item => ({ ...item })) }
        }));
    }

    function setAuctionName(name) {
        auctionName = name || 'Tournament Draw';
        drawWheel();
    }

    function getAssignments() {
        return assignments.map(item => ({ ...item }));
    }

    return { init, setAuctionName, getAssignments, setLocked };
})();
