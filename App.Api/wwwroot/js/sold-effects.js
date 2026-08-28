// Shared, auction-rule-controlled celebration for newly completed player sales.
(function () {
  const settings = { animationEnabled: true, animationStyle: 'Stamp', soundEnabled: true, drawSoundEnabled: true, showSoundUnlock: false };
  let audioContext = null;
  let lastSaleKey = null;
  let unlockButton = null;
  let masterSoundEnabled = true;
  let masterInitialized = false;

  function getAudioContext() {
    if (!audioContext) {
      const AudioContextClass = window.AudioContext || window.webkitAudioContext;
      if (AudioContextClass) audioContext = new AudioContextClass();
    }
    return audioContext;
  }

  async function unlockAudio() {
    const context = getAudioContext();
    if (context?.state === 'suspended') await context.resume();
    updateUnlockButton();
    return context;
  }

  async function toggleMasterSound() {
    const currentlyActive = masterSoundEnabled && audioContext?.state === 'running';
    masterSoundEnabled = !currentlyActive;
    localStorage.setItem('publicDisplaySound', masterSoundEnabled ? 'enabled' : 'disabled');
    if (masterSoundEnabled) await unlockAudio();
    updateUnlockButton();
  }

  function updateUnlockButton() {
    if ((!settings.soundEnabled && !settings.drawSoundEnabled) || !settings.showSoundUnlock) {
      unlockButton?.remove();
      unlockButton = null;
      return;
    }
    if (!unlockButton) {
      unlockButton = document.createElement('button');
      unlockButton.type = 'button';
      unlockButton.className = 'sold-sound-unlock';
      unlockButton.addEventListener('click', () => toggleMasterSound().catch(() => {}));
      document.body.appendChild(unlockButton);
    }
    const active = masterSoundEnabled && audioContext?.state === 'running';
    unlockButton.textContent = active ? 'Disable sound' : 'Enable sound';
    unlockButton.setAttribute('aria-label', active ? 'Disable all sounds on this display' : 'Enable all sounds on this display');
  }

  function strike(context, start, strength) {
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.type = 'triangle';
    oscillator.frequency.setValueAtTime(175, start);
    oscillator.frequency.exponentialRampToValueAtTime(62, start + .13);
    gain.gain.setValueAtTime(.0001, start);
    gain.gain.exponentialRampToValueAtTime(strength, start + .006);
    gain.gain.exponentialRampToValueAtTime(.0001, start + .22);
    oscillator.connect(gain).connect(context.destination);
    oscillator.start(start);
    oscillator.stop(start + .24);
  }

  async function playSoldSound() {
    if (!settings.soundEnabled || !masterSoundEnabled) return;
    const context = await unlockAudio();
    if (!context || context.state !== 'running') {
      updateUnlockButton();
      return;
    }
    const start = context.currentTime + .015;
    strike(context, start, .28);
    strike(context, start + .19, .2);
  }

  function tone(context, frequency, start, duration, strength, type = 'sine') {
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.type = type;
    oscillator.frequency.setValueAtTime(frequency, start);
    gain.gain.setValueAtTime(.0001, start);
    gain.gain.exponentialRampToValueAtTime(strength, start + .008);
    gain.gain.exponentialRampToValueAtTime(.0001, start + duration);
    oscillator.connect(gain).connect(context.destination);
    oscillator.start(start);
    oscillator.stop(start + duration + .02);
  }

  function playDrawTick(step = 0) {
    if (!settings.drawSoundEnabled || !masterSoundEnabled) return;
    const context = getAudioContext();
    if (!context || context.state !== 'running') { updateUnlockButton(); return; }
    tone(context, 330 + ((Number(step) % 4) * 24), context.currentTime + .005, .055, .035, 'sine');
  }

  function playDrawReveal() {
    if (!settings.drawSoundEnabled || !masterSoundEnabled) return;
    const context = getAudioContext();
    if (!context || context.state !== 'running') { updateUnlockButton(); return; }
    const start = context.currentTime + .01;
    // Short winner fanfare: a bright stop cue followed by a rising celebratory chord.
    tone(context, 392.00, start, .13, .075, 'triangle');
    tone(context, 523.25, start + .09, .28, .09, 'sine');
    tone(context, 659.25, start + .19, .34, .085, 'sine');
    tone(context, 783.99, start + .29, .48, .08, 'sine');
    tone(context, 1046.50, start + .39, .62, .055, 'sine');
  }

  function secureRandomUnit() {
    const value = new Uint32Array(1);
    crypto.getRandomValues(value);
    return value[0] / 4294967296;
  }

  function celebrateDraw(playerName, accentColor) {
    playDrawReveal();
    document.querySelector('.draw-winner-celebration')?.remove();

    const overlay = document.createElement('div');
    overlay.className = 'draw-winner-celebration';
    overlay.setAttribute('role', 'status');
    overlay.setAttribute('aria-live', 'assertive');

    const colors = [accentColor || '#06b6d4', '#facc15', '#22c55e', '#f97316', '#ec4899', '#ffffff'];
    const confetti = document.createElement('div');
    confetti.className = 'draw-winner-confetti';
    for (let index = 0; index < 72; index++) {
      const piece = document.createElement('i');
      piece.style.setProperty('--confetti-x', `${secureRandomUnit() * 100}vw`);
      piece.style.setProperty('--confetti-drift', `${(secureRandomUnit() - .5) * 28}vw`);
      piece.style.setProperty('--confetti-delay', `${secureRandomUnit() * .38}s`);
      piece.style.setProperty('--confetti-duration', `${1.45 + secureRandomUnit() * 1.05}s`);
      piece.style.setProperty('--confetti-turn', `${180 + secureRandomUnit() * 900}deg`);
      piece.style.background = colors[Math.floor(secureRandomUnit() * colors.length)];
      piece.classList.toggle('is-round', secureRandomUnit() > .76);
      confetti.appendChild(piece);
    }

    const panel = document.createElement('div');
    panel.className = 'draw-winner-panel';
    const kicker = document.createElement('div');
    kicker.className = 'draw-winner-kicker';
    kicker.textContent = 'NOW BIDDING';
    const name = document.createElement('div');
    name.className = 'draw-winner-name';
    name.textContent = playerName || 'Selected player';
    panel.append(kicker, name);
    overlay.append(confetti, panel);
    document.body.appendChild(overlay);
    window.setTimeout(() => overlay.classList.add('draw-winner-out'), 2050);
    window.setTimeout(() => overlay.remove(), 2450);
  }

  function showAnimation(details) {
    if (!settings.animationEnabled) return;
    document.querySelector('.sold-celebration')?.remove();

    const overlay = document.createElement('div');
    overlay.className = 'sold-celebration';
    overlay.setAttribute('role', 'status');
    overlay.setAttribute('aria-live', 'assertive');
    overlay.style.setProperty('--sold-team-color', details.teamColor || '#16a34a');

    const panel = document.createElement('div');
    panel.className = 'sold-celebration-panel';
    const visual = document.createElement('div');
    if (settings.animationStyle === 'Hammer') {
      visual.className = 'sold-hammer-stage';
      visual.innerHTML = '<div class="sold-hammer" aria-hidden="true">🔨</div><div class="sold-hammer-impact">SOLD</div>';
    } else {
      visual.className = 'sold-stamp';
      visual.textContent = 'SOLD';
    }
    const player = document.createElement('div');
    player.className = 'sold-player-name';
    player.textContent = details.playerName || 'Player';
    const result = document.createElement('div');
    result.className = 'sold-result-line';
    result.textContent = `${details.teamName || 'Winning team'} · ${details.amount || ''}`;

    panel.append(visual, player, result);
    overlay.appendChild(panel);
    document.body.appendChild(overlay);
    window.setTimeout(() => overlay.classList.add('sold-celebration-out'), 3000);
    window.setTimeout(() => overlay.remove(), 3500);
  }

  function configure(options) {
    Object.assign(settings, options || {});
    if (!masterInitialized) {
      const stored = localStorage.getItem('publicDisplaySound');
      masterSoundEnabled = settings.showSoundUnlock ? stored === 'enabled' : true;
      masterInitialized = true;
    }
    updateUnlockButton();
  }

  function celebrate(details) {
    const saleKey = details?.saleId == null ? null : String(details.saleId);
    if (saleKey && saleKey === lastSaleKey) return;
    if (saleKey) lastSaleKey = saleKey;
    playSoldSound();
    showAnimation(details || {});
  }

  // Priming on the first gesture lets the operator console play immediately after a sale.
  document.addEventListener('pointerdown', () => {
    if ((settings.soundEnabled || settings.drawSoundEnabled) && masterSoundEnabled) unlockAudio().catch(() => {});
  }, { once: true, capture: true });

  window.SoldEffects = { configure, celebrate, celebrateDraw, unlockAudio, playDrawTick, playDrawReveal };
})();
