const FixtureGenerator = (() => {
    let auctionId = null;
    let teamCount = 0;
    let auctionTeamCount = 0;
    let state = null;
    let locked = false;

    const el = id => document.getElementById(id);
    const storageKey = () => `fixtureGenerator:${auctionId}`;
    const teamLabel = index => {
        let value = index + 1;
        let letters = '';
        while (value > 0) {
            value--;
            letters = String.fromCharCode(65 + (value % 26)) + letters;
            value = Math.floor(value / 26);
        }
        return `Team ${letters}`;
    };
    const escapeHtml = value => String(value ?? '')
        .replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;').replaceAll("'", '&#039;');
    const toMinutes = value => {
        const [hours, minutes] = String(value || '').split(':').map(Number);
        return Number.isFinite(hours) && Number.isFinite(minutes) ? hours * 60 + minutes : NaN;
    };
    const formatTime = minutes => {
        const value = ((Math.round(minutes) % 1440) + 1440) % 1440;
        const hour = Math.floor(value / 60);
        const minute = value % 60;
        const suffix = hour >= 12 ? 'PM' : 'AM';
        return `${hour % 12 || 12}:${String(minute).padStart(2, '0')} ${suffix}`;
    };
    const addDays = (dateString, days) => {
        const date = new Date(`${dateString}T12:00:00`);
        date.setDate(date.getDate() + days);
        return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
    };

    function getDrawMap() {
        let assignments = [];
        if (window.TournamentDraw?.getAssignments) assignments = TournamentDraw.getAssignments();
        if (!assignments.length) {
            try {
                assignments = JSON.parse(localStorage.getItem(`tournamentDraw:${auctionId}`) || '{}').assignments || [];
            } catch { assignments = []; }
        }
        return new Map(assignments.map(item => [item.slot, item.name]));
    }

    function displayTeam(slot) {
        return getDrawMap().get(slot) || slot;
    }

    function defaultConfig() {
        const matches = Math.max(1, teamCount - 1);
        const total = Math.ceil(teamCount * matches / 2);
        const slots = Math.min(6, Math.max(2, total));
        const days = Math.max(1, Math.ceil(total / slots));
        const today = new Date();
        const startDate = new Date(today.getTime() - today.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
        return {
            matchesPerTeam: matches, days, slotsPerDay: slots, startDate,
            matchSlots: makeDefaultMatchSlots(slots), includeBreak: true, breakStart: '13:00', breakEnd: '14:00',
            knockoutFormat: 'none', semiOneA: 1, semiOneB: Math.min(4, teamCount),
            semiTwoA: Math.min(2, teamCount), semiTwoB: Math.min(3, teamCount),
            semiDate: addDays(startDate, days), semiOneTime: '09:00', semiOneEnd: '11:00', semiTwoTime: '12:00', semiTwoEnd: '14:00',
            eliminatorA: Math.min(2, teamCount), eliminatorB: Math.min(3, teamCount), eliminatorDate: addDays(startDate, days), eliminatorStart: '09:00', eliminatorEnd: '11:00',
            finalA: 1, finalB: Math.min(2, teamCount), finalSeed: 1,
            finalDate: addDays(startDate, days + 1), finalStart: '12:00', finalEnd: '14:00'
        };
    }

    function makeDefaultMatchSlots(count, existing = []) {
        const slots = existing.slice(0, count).map(slot => typeof slot === 'string'
            ? { start: slot, end: formatInputTime(toMinutes(slot) + 90) }
            : { start: slot.start, end: slot.end });
        let minute = slots.length ? toMinutes(slots[slots.length - 1].end) : 9 * 60;
        while (slots.length < count) {
            if ((minute < 13 * 60 && minute + 90 > 13 * 60) || (minute >= 13 * 60 && minute < 14 * 60)) minute = 14 * 60;
            slots.push({ start: formatInputTime(minute), end: formatInputTime(minute + 90) });
            minute += 90;
        }
        return slots;
    }

    function formatInputTime(minutes) {
        return `${String(Math.floor(minutes / 60) % 24).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
    }

    function renderMatchTimeInputs(count, values = []) {
        const safeCount = Number.isInteger(count) && count > 0 ? count : 1;
        const slots = makeDefaultMatchSlots(safeCount, values);
        el('fixtureMatchTimes').innerHTML = slots.map((slot, index) => `<div class="fixture-match-time-row">
            <label>Match ${index + 1}</label>
            <input class="form-control fixture-match-start" aria-label="Match ${index + 1} start" type="time" value="${escapeHtml(slot.start)}" required>
            <span>to</span>
            <input class="form-control fixture-match-end" aria-label="Match ${index + 1} end" type="time" value="${escapeHtml(slot.end)}" required>
        </div>`).join('');
    }

    function readConfig() {
        return {
            matchesPerTeam: Number(el('fixtureMatchesPerTeam').value),
            days: Number(el('fixtureDays').value),
            slotsPerDay: Number(el('fixtureSlotsPerDay').value),
            startDate: el('fixtureStartDate').value,
            matchSlots: [...document.querySelectorAll('.fixture-match-time-row')].map(row => ({
                start: row.querySelector('.fixture-match-start').value,
                end: row.querySelector('.fixture-match-end').value
            })),
            includeBreak: el('fixtureIncludeBreak').checked,
            breakStart: el('fixtureBreakStart').value,
            breakEnd: el('fixtureBreakEnd').value,
            knockoutFormat: el('fixtureKnockoutFormat').value,
            semiOneA: Number(el('fixtureSemiOneA').value),
            semiOneB: Number(el('fixtureSemiOneB').value),
            semiTwoA: Number(el('fixtureSemiTwoA').value),
            semiTwoB: Number(el('fixtureSemiTwoB').value),
            semiDate: el('fixtureSemiDate').value,
            semiOneTime: el('fixtureSemiOneTime').value,
            semiOneEnd: el('fixtureSemiOneEnd').value,
            semiTwoTime: el('fixtureSemiTwoTime').value,
            semiTwoEnd: el('fixtureSemiTwoEnd').value,
            eliminatorA: Number(el('fixtureEliminatorA').value),
            eliminatorB: Number(el('fixtureEliminatorB').value),
            eliminatorDate: el('fixtureEliminatorDate').value,
            eliminatorStart: el('fixtureEliminatorStart').value,
            eliminatorEnd: el('fixtureEliminatorEnd').value,
            finalA: Number(el('fixtureFinalA').value),
            finalB: Number(el('fixtureFinalB').value),
            finalSeed: Number(el('fixtureFinalSeed').value),
            finalDate: el('fixtureFinalDate').value,
            finalStart: el('fixtureFinalStart').value,
            finalEnd: el('fixtureFinalEnd').value
        };
    }

    function writeConfig(config) {
        el('fixtureTeams').value = teamCount;
        el('fixtureTeamCount').textContent = teamCount;
        el('fixtureMatchesPerTeam').value = config.matchesPerTeam;
        el('fixtureDays').value = config.days;
        el('fixtureSlotsPerDay').value = config.slotsPerDay;
        el('fixtureStartDate').value = config.startDate || new Date().toISOString().slice(0, 10);
        const legacySlots = config.matchSlots || config.matchTimes || [];
        renderMatchTimeInputs(config.slotsPerDay, legacySlots);
        el('fixtureIncludeBreak').checked = config.includeBreak ?? Boolean(config.breakStart && config.breakEnd);
        el('fixtureBreakStart').value = config.breakStart;
        el('fixtureBreakEnd').value = config.breakEnd;
        el('fixtureKnockoutFormat').value = config.knockoutFormat || (config.includeSemis ? 'semifinals' : 'none');
        el('fixtureSemiOneA').value = config.semiOneA || 1;
        el('fixtureSemiOneB').value = config.semiOneB || Math.min(4, teamCount);
        el('fixtureSemiTwoA').value = config.semiTwoA || Math.min(2, teamCount);
        el('fixtureSemiTwoB').value = config.semiTwoB || Math.min(3, teamCount);
        updateSeedLimits();
        el('fixtureSemiDate').value = config.semiDate || '';
        el('fixtureSemiOneTime').value = config.semiOneTime || '10:00';
        el('fixtureSemiOneEnd').value = config.semiOneEnd || formatInputTime(toMinutes(config.semiOneTime || '10:00') + 120);
        el('fixtureSemiTwoTime').value = config.semiTwoTime || '14:00';
        el('fixtureSemiTwoEnd').value = config.semiTwoEnd || formatInputTime(toMinutes(config.semiTwoTime || '14:00') + 120);
        el('fixtureEliminatorA').value = config.eliminatorA || Math.min(2, teamCount);
        el('fixtureEliminatorB').value = config.eliminatorB || Math.min(3, teamCount);
        el('fixtureEliminatorDate').value = config.eliminatorDate || config.semiDate || '';
        el('fixtureEliminatorStart').value = config.eliminatorStart || '09:00';
        el('fixtureEliminatorEnd').value = config.eliminatorEnd || '11:00';
        el('fixtureFinalA').value = config.finalA || 1;
        el('fixtureFinalB').value = config.finalB || Math.min(2, teamCount);
        el('fixtureFinalSeed').value = config.finalSeed || 1;
        const legacyFinal = config.finalDateTime || '';
        el('fixtureFinalDate').value = config.finalDate || legacyFinal.slice(0, 10);
        el('fixtureFinalStart').value = config.finalStart || legacyFinal.slice(11, 16) || '12:00';
        el('fixtureFinalEnd').value = config.finalEnd || formatInputTime(toMinutes(config.finalStart || legacyFinal.slice(11, 16) || '12:00') + 120);
        toggleBreakSettings();
        toggleKnockoutSettings();
    }

    function validate(config) {
        if (!Number.isInteger(teamCount) || teamCount < 2) return 'Number of teams must be a whole number of at least 2.';
        if (!Number.isInteger(config.matchesPerTeam) || config.matchesPerTeam < 1) return 'League matches per team must be a whole number of at least 1.';
        if (!Number.isInteger(config.days) || config.days < 1) return 'Number of days must be a whole number of at least 1.';
        if (!Number.isInteger(config.slotsPerDay) || config.slotsPerDay < 1) return 'Matches per day must be a whole number of at least 1.';
        if ((teamCount * config.matchesPerTeam) % 2 !== 0) return `${teamCount} teams cannot each play ${config.matchesPerTeam} matches because the total number of team appearances is odd.`;
        const totalMatches = teamCount * config.matchesPerTeam / 2;
        if (config.days * config.slotsPerDay < totalMatches) return `The league needs ${totalMatches} slots, but this setup only provides ${config.days * config.slotsPerDay}. Add days or matches per day.`;
        if (!config.startDate) return 'Enter the league start date.';
        if (config.matchSlots.length !== config.slotsPerDay) return 'Enter a start and end time for every match slot.';
        const matchStarts = config.matchSlots.map(slot => toMinutes(slot.start));
        if (config.matchSlots.some(slot => !Number.isFinite(toMinutes(slot.start)) || !Number.isFinite(toMinutes(slot.end)) || toMinutes(slot.end) <= toMinutes(slot.start))) return 'Every match end time must be later than its start time.';
        if (matchStarts.some((minute, index) => index > 0 && minute < toMinutes(config.matchSlots[index - 1].end))) return 'Match time ranges cannot overlap and must be in chronological order.';
        if (config.includeBreak && (!config.breakStart || !config.breakEnd)) return 'Enter both the start and end of the enabled break.';
        if (config.includeBreak && (toMinutes(config.breakEnd) <= toMinutes(config.breakStart))) return 'Break end time must be later than its start time.';
        if (config.includeBreak && config.matchSlots.some(slot => toMinutes(slot.start) < toMinutes(config.breakEnd) && toMinutes(slot.end) > toMinutes(config.breakStart))) return 'A match overlaps the selected break. Move that match or adjust the break.';
        if (config.knockoutFormat === 'semifinals' && teamCount < 4) return 'Semifinals require at least four teams.';
        if (config.knockoutFormat === 'semifinals') {
            const seeds = [config.semiOneA, config.semiOneB, config.semiTwoA, config.semiTwoB];
            if (seeds.some(seed => !Number.isInteger(seed) || seed < 1 || seed > teamCount)) return `Semifinal seeds must be whole numbers from 1 to ${teamCount}.`;
            if (new Set(seeds).size !== seeds.length) return 'Each semifinal seed can only be used once.';
            if (!config.semiDate || !config.semiOneTime || !config.semiOneEnd || !config.semiTwoTime || !config.semiTwoEnd) return 'Enter the semifinal date and both time ranges.';
            if (toMinutes(config.semiOneEnd) <= toMinutes(config.semiOneTime) || toMinutes(config.semiTwoEnd) <= toMinutes(config.semiTwoTime)) return 'Each semifinal end time must be later than its start time.';
        }
        if (config.knockoutFormat === 'eliminator') {
            const seeds = [config.eliminatorA, config.eliminatorB, config.finalSeed];
            if (seeds.some(seed => !Number.isInteger(seed) || seed < 1 || seed > teamCount)) return `Eliminator and final seeds must be whole numbers from 1 to ${teamCount}.`;
            if (new Set(seeds).size !== seeds.length) return 'The seeded finalist cannot also play in the eliminator, and eliminator seeds must differ.';
            if (!config.eliminatorDate || !config.eliminatorStart || !config.eliminatorEnd || toMinutes(config.eliminatorEnd) <= toMinutes(config.eliminatorStart)) return 'Enter a valid eliminator date and time range.';
        }
        if (config.knockoutFormat === 'none') {
            if (![config.finalA, config.finalB].every(seed => Number.isInteger(seed) && seed >= 1 && seed <= teamCount) || config.finalA === config.finalB) return `Choose two different final seeds from 1 to ${teamCount}.`;
        }
        if (!config.finalDate || !config.finalStart || !config.finalEnd || toMinutes(config.finalEnd) <= toMinutes(config.finalStart)) return 'Enter a valid final date and time range.';
        const leagueEnd = new Date(`${addDays(config.startDate, config.days - 1)}T${config.matchSlots[config.matchSlots.length - 1].end}`);
        const firstPlayoff = config.knockoutFormat === 'semifinals' ? `${config.semiDate}T${config.semiOneTime}` : config.knockoutFormat === 'eliminator' ? `${config.eliminatorDate}T${config.eliminatorStart}` : `${config.finalDate}T${config.finalStart}`;
        if (new Date(firstPlayoff) <= leagueEnd) return 'The playoff or final must be scheduled after the league schedule ends.';
        const latestQualifier = config.knockoutFormat === 'semifinals' ? `${config.semiDate}T${config.semiTwoEnd}` : config.knockoutFormat === 'eliminator' ? `${config.eliminatorDate}T${config.eliminatorEnd}` : null;
        if (latestQualifier && new Date(latestQualifier) >= new Date(`${config.finalDate}T${config.finalStart}`)) return 'The final must be scheduled after the semifinal or eliminator.';
        return null;
    }

    function buildPairings(count, matchesPerTeam) {
        const pairs = [];
        const fullCycles = Math.floor(matchesPerTeam / (count - 1));
        const remainder = matchesPerTeam % (count - 1);
        for (let cycle = 0; cycle < fullCycles; cycle++) {
            for (let a = 0; a < count; a++) {
                for (let b = a + 1; b < count; b++) pairs.push({ a, b, repeat: cycle });
            }
        }
        for (let distance = 1; distance <= Math.floor(remainder / 2); distance++) {
            for (let a = 0; a < count; a++) pairs.push({ a, b: (a + distance) % count, repeat: fullCycles });
        }
        if (remainder % 2 === 1) {
            for (let a = 0; a < count / 2; a++) pairs.push({ a, b: a + count / 2, repeat: fullCycles });
        }
        return pairs;
    }

    function createSlots(config, matchCount) {
        const capacity = config.days * config.slotsPerDay;
        const selected = [];
        for (let index = 0; index < matchCount; index++) {
            selected.push(matchCount === 1 ? 0 : Math.round(index * (capacity - 1) / (matchCount - 1)));
        }
        return selected.map(slotIndex => {
            const day = Math.floor(slotIndex / config.slotsPerDay);
            const position = slotIndex % config.slotsPerDay;
            const matchSlot = config.matchSlots[position];
            const minute = toMinutes(matchSlot.start);
            const endMinute = toMinutes(matchSlot.end);
            return { slotIndex, day, position, minute, endMinute, date: addDays(config.startDate, day), absoluteMinute: day * 1440 + minute, absoluteEndMinute: day * 1440 + endMinute };
        });
    }

    function orderPairings(pairings, slots, config) {
        const remaining = pairings.map((pair, index) => ({ ...pair, sourceIndex: index }));
        const teamState = Array.from({ length: teamCount }, () => ({ lastSlot: -1000, dayCounts: {}, early: 0, late: 0 }));
        const opponentLast = new Map();
        const scheduled = [];
        slots.forEach(slot => {
            let bestIndex = 0;
            let bestScore = Infinity;
            const opponentPass = Math.min(...remaining.map(pair => pair.repeat));
            remaining.forEach((pair, index) => {
                if (pair.repeat !== opponentPass) return;
                const first = teamState[pair.a];
                const second = teamState[pair.b];
                const gapA = slot.slotIndex - first.lastSlot;
                const gapB = slot.slotIndex - second.lastSlot;
                let score = 0;
                if (gapA === 1 || gapB === 1) score += 100000;
                score += (first.dayCounts[slot.day] || 0) * 600 + (second.dayCounts[slot.day] || 0) * 600;
                score += 160 / Math.max(gapA, 1) + 160 / Math.max(gapB, 1);
                if (slot.position === 0) score += (first.early + second.early) * 90;
                if (slot.position === config.slotsPerDay - 1) score += (first.late + second.late) * 90;
                const key = `${Math.min(pair.a, pair.b)}-${Math.max(pair.a, pair.b)}`;
                score += 80 / Math.max(scheduled.length - (opponentLast.get(key) ?? -100), 1);
                score += pair.sourceIndex / 100000;
                if (score < bestScore) { bestScore = score; bestIndex = index; }
            });
            const pair = remaining.splice(bestIndex, 1)[0];
            [pair.a, pair.b].forEach(team => {
                const current = teamState[team];
                current.lastSlot = slot.slotIndex;
                current.dayCounts[slot.day] = (current.dayCounts[slot.day] || 0) + 1;
                if (slot.position === 0) current.early++;
                if (slot.position === config.slotsPerDay - 1) current.late++;
            });
            opponentLast.set(`${Math.min(pair.a, pair.b)}-${Math.max(pair.a, pair.b)}`, scheduled.length);
            scheduled.push({ stage: 'League', teamA: teamLabel(pair.a), teamB: teamLabel(pair.b), ...slot });
        });
        return scheduled;
    }

    function buildReport(fixtures, config) {
        const league = fixtures.filter(match => match.stage === 'League');
        const report = Array.from({ length: teamCount }, (_, index) => ({
            slot: teamLabel(index), matches: 0, opponents: new Set(), repeats: 0,
            minRestMinutes: Infinity, early: 0, late: 0, dayCounts: {}, lastEndMinute: null,
            backToBack: 0
        }));
        league.forEach(match => {
            // Use slot lookup rather than letter arithmetic so Team AA and beyond remain correct.
            const a = Array.from({ length: teamCount }, (_, i) => teamLabel(i)).indexOf(match.teamA);
            const b = Array.from({ length: teamCount }, (_, i) => teamLabel(i)).indexOf(match.teamB);
            [[a, b], [b, a]].forEach(([team, opponent]) => {
                const item = report[team];
                item.matches++;
                if (item.opponents.has(opponent)) item.repeats++;
                item.opponents.add(opponent);
                item.dayCounts[match.day] = (item.dayCounts[match.day] || 0) + 1;
                if (match.position === 0) item.early++;
                if (match.position === config.slotsPerDay - 1) item.late++;
                if (item.lastEndMinute !== null) {
                    const rest = match.absoluteMinute - item.lastEndMinute;
                    item.minRestMinutes = Math.min(item.minRestMinutes, rest);
                    if (match.slotIndex - item.lastSlotIndex === 1) item.backToBack++;
                }
                item.lastEndMinute = match.absoluteEndMinute;
                item.lastSlotIndex = match.slotIndex;
            });
        });
        return report.map(item => ({
            ...item,
            opponents: item.opponents.size,
            minRestMinutes: Number.isFinite(item.minRestMinutes) ? item.minRestMinutes : null,
            busiestDay: Math.max(0, ...Object.values(item.dayCounts))
        }));
    }

    function addKnockoutFixtures(fixtures, config) {
        if (config.knockoutFormat === 'semifinals') {
            fixtures.push({ stage: 'Semifinal 1', teamA: `Seed ${config.semiOneA}`, teamB: `Seed ${config.semiOneB}`, date: config.semiDate, minute: toMinutes(config.semiOneTime), endMinute: toMinutes(config.semiOneEnd) });
            fixtures.push({ stage: 'Semifinal 2', teamA: `Seed ${config.semiTwoA}`, teamB: `Seed ${config.semiTwoB}`, date: config.semiDate, minute: toMinutes(config.semiTwoTime), endMinute: toMinutes(config.semiTwoEnd) });
        } else if (config.knockoutFormat === 'eliminator') {
            fixtures.push({ stage: 'Eliminator', teamA: `Seed ${config.eliminatorA}`, teamB: `Seed ${config.eliminatorB}`, date: config.eliminatorDate, minute: toMinutes(config.eliminatorStart), endMinute: toMinutes(config.eliminatorEnd) });
        }
        fixtures.push({
            stage: 'Final',
            teamA: config.knockoutFormat === 'semifinals' ? 'Semifinal 1 Winner' : config.knockoutFormat === 'eliminator' ? `Seed ${config.finalSeed}` : `Seed ${config.finalA}`,
            teamB: config.knockoutFormat === 'semifinals' ? 'Semifinal 2 Winner' : config.knockoutFormat === 'eliminator' ? 'Eliminator Winner' : `Seed ${config.finalB}`,
            date: config.finalDate,
            minute: toMinutes(config.finalStart),
            endMinute: toMinutes(config.finalEnd)
        });
    }

    function generate(config) {
        const pairings = buildPairings(teamCount, config.matchesPerTeam);
        const slots = createSlots(config, pairings.length);
        const fixtures = orderPairings(pairings, slots, config);
        const report = buildReport(fixtures, config);
        addKnockoutFixtures(fixtures, config);
        return { teamCount, config, fixtures, report, generatedAt: new Date().toISOString() };
    }

    function formatRest(minutes) {
        if (minutes === null) return '—';
        if (minutes >= 1440) return `${Math.floor(minutes / 1440)}d ${Math.round((minutes % 1440) / 60)}h`;
        const hours = Math.floor(minutes / 60);
        const mins = minutes % 60;
        return hours ? `${hours}h${mins ? ` ${mins}m` : ''}` : `${mins}m`;
    }

    function render() {
        const hasState = state?.fixtures?.length;
        el('fixtureEmpty').classList.toggle('d-none', Boolean(hasState));
        el('fixtureResults').classList.toggle('d-none', !hasState);
        el('drawFixtureEmpty').classList.toggle('d-none', Boolean(hasState));
        el('drawFixtureTableWrap').classList.toggle('d-none', !hasState);
        const assignedCount = Math.min(state?.teamCount || teamCount, getDrawMap().size);
        const fixtureTeams = state?.teamCount || teamCount;
        el('drawFixtureProgress').textContent = `${assignedCount} / ${fixtureTeams} teams assigned`;
        if (!hasState) {
            el('drawFixtureTableBody').innerHTML = '';
            updateLockUi();
            return;
        }
        const rows = state.fixtures.map(match => ({ ...match }));
        if (state.config.includeBreak) {
            const leagueDates = [...new Set(state.fixtures.filter(match => match.stage === 'League').map(match => match.date))];
            leagueDates.forEach(date => rows.push({ stage: 'Break', date, minute: toMinutes(state.config.breakStart), endMinute: toMinutes(state.config.breakEnd) }));
        }
        rows.sort((a, b) => `${a.date}T${String(a.minute).padStart(4, '0')}`.localeCompare(`${b.date}T${String(b.minute).padStart(4, '0')}`));
        let leagueNumber = 0;
        const fixtureRowsHtml = rows.map(match => {
            const dateValue = new Date(`${match.date}T12:00:00`);
            const date = dateValue.toLocaleDateString([], { month: 'numeric', day: 'numeric', year: 'numeric' });
            const day = dateValue.toLocaleDateString([], { weekday: 'long' });
            const time = `${formatTime(match.minute)} – ${formatTime(match.endMinute)}`;
            if (match.stage === 'Break') return `<tr class="fixture-break-row"><td>${escapeHtml(date)}</td><td>${escapeHtml(day)}</td><td class="text-nowrap">${escapeHtml(time)}</td><td colspan="2"><strong>Break / lunch</strong></td></tr>`;
            const teamA = match.stage === 'League' ? displayTeam(match.teamA) : match.teamA;
            const teamB = match.stage === 'League' ? displayTeam(match.teamB) : match.teamB;
            const stageLabel = match.stage === 'League' ? `League Game ${++leagueNumber}` : match.stage;
            const stageClass = match.stage === 'Final' ? 'fixture-stage-final' : match.stage.startsWith('Semifinal') || match.stage === 'Eliminator' ? 'fixture-stage-semi' : 'fixture-stage-league';
            return `<tr><td>${escapeHtml(date)}</td><td>${escapeHtml(day)}</td><td class="text-nowrap">${escapeHtml(time)}</td><td><span class="fixture-stage ${stageClass}">${escapeHtml(stageLabel)}</span></td><td><strong>${escapeHtml(teamA)}</strong><span class="fixture-versus">vs</span><strong>${escapeHtml(teamB)}</strong></td></tr>`;
        }).join('');
        el('fixtureTableBody').innerHTML = fixtureRowsHtml;
        el('drawFixtureTableBody').innerHTML = fixtureRowsHtml;

        const totalLeague = state.fixtures.filter(match => match.stage === 'League').length;
        const backToBack = state.report.reduce((sum, team) => sum + team.backToBack, 0);
        const minRest = Math.min(...state.report.map(team => team.minRestMinutes ?? Infinity));
        const repeatValues = state.report.map(team => team.repeats);
        const repeatSpread = Math.max(...repeatValues) - Math.min(...repeatValues);
        el('fixtureSummary').innerHTML = [
            ['League matches', totalLeague],
            ['Matches per team', state.config.matchesPerTeam],
            ['Shortest rest', Number.isFinite(minRest) ? formatRest(minRest) : '—'],
            ['Back-to-backs', backToBack]
        ].map(([label, value]) => `<div class="fixture-summary"><strong>${escapeHtml(value)}</strong><span>${escapeHtml(label)}</span></div>`).join('');

        const warnings = [];
        if (backToBack) warnings.push(`${backToBack} back-to-back team appearance${backToBack === 1 ? '' : 's'} could not be avoided with the available slots.`);
        if (repeatSpread > 0) warnings.push(`Repeat-opponent counts differ by ${repeatSpread}; the requested format prevents a perfectly equal repeat split.`);
        const busiest = state.report.map(team => team.busiestDay);
        const busiestSpread = Math.max(...busiest) - Math.min(...busiest);
        if (busiestSpread > 0) warnings.push(`Peak daily loads differ by ${busiestSpread} match${busiestSpread === 1 ? '' : 'es'}; the selected day and slot limits prevent an equal split.`);
        el('fixtureWarnings').innerHTML = warnings.length
            ? `<div class="alert alert-warning mb-0"><strong>Fairness notes</strong><ul class="mb-0 mt-1">${warnings.map(item => `<li>${escapeHtml(item)}</li>`).join('')}</ul></div>`
            : '<div class="alert alert-success mb-0"><strong>Balanced schedule:</strong> no back-to-back games or material fairness warnings were found.</div>';
        el('fixtureFairnessBody').innerHTML = state.report.map(item => `<tr>
            <td><strong>${escapeHtml(displayTeam(item.slot))}</strong></td><td>${item.matches}</td><td>${item.opponents}</td><td>${item.repeats}</td>
            <td>${escapeHtml(formatRest(item.minRestMinutes))}</td><td>${item.early} / ${item.late}</td><td>${item.busiestDay}</td>
        </tr>`).join('');
        updateLockUi();
    }

    function updateLockUi() {
        const form = el('fixtureForm');
        form.querySelectorAll('input, select, button').forEach(control => { control.disabled = locked; });
        const lockButton = el('fixtureLockBtn');
        lockButton.disabled = locked || !state?.fixtures?.length;
        lockButton.textContent = locked ? 'Fixtures Locked' : 'Lock Fixtures';
    }

    function setLocked(value) {
        locked = Boolean(value);
        updateLockUi();
    }

    async function lockFixtures() {
        if (locked || !state?.fixtures?.length) return;
        try {
            await Api.post(`/api/auctions/${auctionId}/planning/fixtures/lock`);
            setLocked(true);
            window.notify?.('Fixtures locked. Unlock them from Auction Lifecycle.', 'success');
            window.dispatchEvent(new CustomEvent('planning-locks-updated'));
        } catch (error) {
            window.notify?.(error.message, 'error');
        }
    }

    async function save() {
        // The database is authoritative. Keep localStorage only as a recovery copy.
        await Api.put(`/api/auctions/${auctionId}/planning/fixtures`, { state });
        localStorage.setItem(storageKey(), JSON.stringify(state));
    }

    function normalizeSavedState(saved) {
        if (!saved || !Array.isArray(saved.fixtures)) return null;

        const savedTeamCount = Number.isInteger(saved.teamCount) && saved.teamCount >= 2
            ? saved.teamCount
            : teamCount;
        const defaults = defaultConfig();
        const config = { ...defaults, ...(saved.config || {}) };
        const legacySlots = config.matchSlots || config.matchTimes || [];
        config.matchSlots = makeDefaultMatchSlots(
            Number.isInteger(config.slotsPerDay) && config.slotsPerDay > 0 ? config.slotsPerDay : defaults.slotsPerDay,
            legacySlots);
        config.startDate ||= defaults.startDate;

        const fixtures = saved.fixtures.map((match, index) => {
            const day = Number.isInteger(match.day) ? match.day : 0;
            const position = Number.isInteger(match.position)
                ? match.position
                : (Number.isInteger(match.slotIndex) ? match.slotIndex % config.slotsPerDay : index % config.slotsPerDay);
            const slot = config.matchSlots[Math.min(position, config.matchSlots.length - 1)];
            const minute = Number.isFinite(match.minute) ? match.minute : toMinutes(slot?.start);
            const endMinute = Number.isFinite(match.endMinute) ? match.endMinute : toMinutes(slot?.end);
            return {
                ...match,
                day,
                position,
                slotIndex: Number.isInteger(match.slotIndex) ? match.slotIndex : day * config.slotsPerDay + position,
                date: match.date || addDays(config.startDate, day),
                minute,
                endMinute,
                absoluteMinute: Number.isFinite(match.absoluteMinute) ? match.absoluteMinute : day * 1440 + minute,
                absoluteEndMinute: Number.isFinite(match.absoluteEndMinute) ? match.absoluteEndMinute : day * 1440 + endMinute
            };
        });

        const originalTeamCount = teamCount;
        teamCount = savedTeamCount;
        const report = Array.isArray(saved.report) && saved.report.length
            ? saved.report
            : buildReport(fixtures, config);
        teamCount = originalTeamCount;
        return { ...saved, teamCount: savedTeamCount, config, fixtures, report };
    }

    function toggleKnockoutSettings() {
        const format = el('fixtureKnockoutFormat').value;
        el('fixtureSemiSettings').classList.toggle('d-none', format !== 'semifinals');
        el('fixtureEliminatorSettings').classList.toggle('d-none', format !== 'eliminator');
        el('fixtureDirectFinalSeeds').classList.toggle('d-none', format !== 'none');
        el('fixtureEliminatorFinalSeeds').classList.toggle('d-none', format !== 'eliminator');
        el('fixtureSemiFinalLabel').classList.toggle('d-none', format !== 'semifinals');
    }

    function toggleBreakSettings() {
        const enabled = el('fixtureIncludeBreak').checked;
        el('fixtureBreakSettings').classList.toggle('d-none', !enabled);
        el('fixtureBreakStart').required = enabled;
        el('fixtureBreakEnd').required = enabled;
    }

    function updateSeedLimits() {
        ['fixtureSemiOneA', 'fixtureSemiOneB', 'fixtureSemiTwoA', 'fixtureSemiTwoB', 'fixtureEliminatorA', 'fixtureEliminatorB', 'fixtureFinalA', 'fixtureFinalB', 'fixtureFinalSeed'].forEach(id => {
            el(id).max = Math.max(2, teamCount);
        });
    }

    function updateTeamCount() {
        const value = Number(el('fixtureTeams').value);
        if (Number.isInteger(value) && value >= 2) teamCount = value;
        el('fixtureTeamCount').textContent = Number.isInteger(value) && value >= 2 ? value : '—';
        updateSeedLimits();
    }

    async function reset() {
        try {
            await Api.put(`/api/auctions/${auctionId}/planning/fixtures`, { state: null });
            state = null;
            localStorage.removeItem(storageKey());
            teamCount = auctionTeamCount;
            writeConfig(defaultConfig());
            render();
            window.notify?.('Fixture generator reset.', 'success');
        } catch (error) {
            console.error('Unable to reset fixtures', error);
            window.notify?.('Fixtures were not reset because the database could not be updated.', 'error');
        }
    }

    async function init(id) {
        auctionId = String(id);
        try {
            const locks = await Api.get(`/api/auctions/${auctionId}/planning/locks`);
            locked = locks.fixturesLocked === true;
        } catch (error) { console.error('Unable to load planning locks', error); }
        try {
            const teams = await Api.get(`/api/auctions/${auctionId}/teams`);
            auctionTeamCount = teams.length;
            teamCount = auctionTeamCount;
        } catch (error) {
            console.error('Unable to load teams for fixture generator', error);
        }
        try {
            state = normalizeSavedState(await Api.get(`/api/auctions/${auctionId}/planning/fixtures`));
            if (!state) {
                state = normalizeSavedState(JSON.parse(localStorage.getItem(storageKey()) || 'null'));
                if (state) {
                    try {
                        await save();
                        window.notify?.('Recovered fixtures and saved them to the database.', 'success');
                    } catch (error) {
                        console.error('Unable to save recovered fixtures', error);
                        window.notify?.('Fixtures were recovered locally but could not be saved to the database.', 'warning');
                    }
                }
            }
        } catch {
            try { state = normalizeSavedState(JSON.parse(localStorage.getItem(storageKey()) || 'null')); } catch { state = null; }
        }
        if (Number.isInteger(state?.teamCount) && state.teamCount >= 2) teamCount = state.teamCount;
        writeConfig(state?.config || defaultConfig());
        render();
        el('fixtureIncludeBreak').addEventListener('change', toggleBreakSettings);
        el('fixtureKnockoutFormat').addEventListener('change', toggleKnockoutSettings);
        el('fixtureTeams').addEventListener('input', updateTeamCount);
        el('fixtureSlotsPerDay').addEventListener('input', () => {
            const current = [...document.querySelectorAll('.fixture-match-time-row')].map(row => ({ start: row.querySelector('.fixture-match-start').value, end: row.querySelector('.fixture-match-end').value }));
            renderMatchTimeInputs(Number(el('fixtureSlotsPerDay').value), current);
        });
        el('fixtureResetBtn').addEventListener('click', () => void reset());
        el('fixtureLockBtn').addEventListener('click', lockFixtures);
        el('fixtureForm').addEventListener('submit', async event => {
            event.preventDefault();
            updateTeamCount();
            const config = readConfig();
            const error = validate(config);
            if (error) { window.notify ? notify(error, 'warning') : alert(error); return; }
            state = generate(config);
            render();
            try {
                await save();
                window.notify?.('Fixtures generated and saved to the database.', 'success');
            } catch (error) {
                console.error('Unable to save fixtures', error);
                window.notify?.('Fixtures were generated but were not saved to the database. Please try Generate Fixtures again.', 'error');
            }
        });
        window.addEventListener('tournament-draw-updated', event => {
            if (String(event.detail?.auctionId) === auctionId) render();
        });
    }

    return { init, setLocked };
})();
