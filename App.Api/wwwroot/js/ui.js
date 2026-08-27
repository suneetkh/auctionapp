// Shared toast/modal UI to replace the browser's native alert()/confirm()/prompt().
// Requires Bootstrap 5 CSS + JS bundle to already be loaded on the page.
(function () {
    function ensureNoticeStyles() {
        if (document.getElementById('ui-notice-styles')) return;
        const style = document.createElement('style');
        style.id = 'ui-notice-styles';
        style.textContent = `
            .auction-toast-stack{position:fixed;right:1rem;bottom:1rem;z-index:1090;width:min(390px,calc(100vw - 2rem));display:flex;flex-direction:column;gap:.65rem;pointer-events:none}
            .auction-toast{display:grid;grid-template-columns:30px minmax(0,1fr) 24px;align-items:start;gap:.65rem;padding:.85rem .8rem;border:1px solid rgba(220,53,69,.24);border-left:4px solid #dc3545;border-radius:.75rem;background:rgba(255,255,255,.98);color:#282c34;box-shadow:0 14px 36px rgba(26,32,44,.22);opacity:0;transform:translateX(18px);transition:opacity .2s ease,transform .2s ease;pointer-events:auto}
            .auction-toast.show{opacity:1;transform:translateX(0)}
            .auction-toast-success{border-color:rgba(25,135,84,.24);border-left-color:#198754}
            .auction-toast-warning{border-color:rgba(245,158,11,.28);border-left-color:#f59e0b}
            .auction-toast-info{border-color:rgba(14,165,233,.25);border-left-color:#0ea5e9}
            .auction-toast-icon{width:28px;height:28px;display:inline-flex;align-items:center;justify-content:center;border-radius:50%;background:#fde8eb;color:#b42332;font-weight:900}
            .auction-toast-success .auction-toast-icon{background:#e2f5e9;color:#146c43}
            .auction-toast-warning .auction-toast-icon{background:#fff3d6;color:#9a6700}
            .auction-toast-info .auction-toast-icon{background:#e0f2fe;color:#0369a1}
            .auction-toast-message{padding-top:.18rem;font-size:.9rem;font-weight:650;line-height:1.35}
            .auction-toast-close{border:0;background:transparent;color:#747b86;font-size:1.25rem;line-height:1;padding:0}
            @media(max-width:575.98px){.auction-toast-stack{right:.65rem;bottom:.65rem;width:calc(100vw - 1.3rem)}}`;
        document.head.appendChild(style);
    }

    function ensureToastContainer() {
        ensureNoticeStyles();
        let el = document.getElementById('ui-notice-stack') || document.getElementById('auctionToastStack');
        if (!el) {
            el = document.createElement('div');
            el.id = 'ui-notice-stack';
            el.className = 'auction-toast-stack';
            el.setAttribute('aria-live', 'assertive');
            el.setAttribute('aria-atomic', 'false');
            document.body.appendChild(el);
        }
        return el;
    }

    function notify(message, variant) {
        variant = variant || 'error';
        if (variant === 'danger') variant = 'error';
        if (variant === 'dark') variant = 'warning';
        if (!['success', 'warning', 'error', 'info'].includes(variant)) variant = 'info';
        const container = ensureToastContainer();
        const notice = document.createElement('div');
        notice.className = `auction-toast auction-toast-${variant}`;
        notice.setAttribute('role', variant === 'error' ? 'alert' : 'status');

        const icon = document.createElement('span');
        icon.className = 'auction-toast-icon';
        icon.textContent = variant === 'success' ? '✓' : variant === 'warning' ? '!' : variant === 'info' ? 'i' : '×';
        const text = document.createElement('div');
        text.className = 'auction-toast-message';
        text.textContent = String(message);
        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'auction-toast-close';
        close.setAttribute('aria-label', 'Dismiss message');
        close.textContent = '×';
        close.addEventListener('click', () => notice.remove());

        notice.append(icon, text, close);
        container.appendChild(notice);
        requestAnimationFrame(() => notice.classList.add('show'));
        setTimeout(() => {
            notice.classList.remove('show');
            setTimeout(() => notice.remove(), 220);
        }, 6500);
        return notice;
    }

    function clearNotices() {
        const container = document.getElementById('ui-notice-stack') || document.getElementById('auctionToastStack');
        if (container) container.replaceChildren();
    }

    function ensureDialogModal() {
        let el = document.getElementById('ui-dialog-modal');
        if (el) return el;
        el = document.createElement('div');
        el.id = 'ui-dialog-modal';
        el.className = 'modal fade';
        el.tabIndex = -1;
        el.innerHTML = `
            <div class="modal-dialog">
              <div class="modal-content">
                <div class="modal-header">
                  <h5 class="modal-title" id="ui-dialog-title">Confirm</h5>
                  <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                  <p id="ui-dialog-message" class="mb-2"></p>
                  <input type="text" class="form-control d-none" id="ui-dialog-input">
                </div>
                <div class="modal-footer">
                  <button type="button" class="btn btn-secondary" id="ui-dialog-cancel" data-bs-dismiss="modal">Cancel</button>
                  <button type="button" class="btn btn-primary" id="ui-dialog-ok">OK</button>
                </div>
              </div>
            </div>`;
        document.body.appendChild(el);
        return el;
    }

    // Promise-based replacement for confirm(). Callers must `await` it.
    function uiConfirm(message, opts) {
        opts = opts || {};
        return new Promise((resolve) => {
            const el = ensureDialogModal();
            el.querySelector('#ui-dialog-title').textContent = opts.title || 'Please confirm';
            el.querySelector('#ui-dialog-message').textContent = message;
            const input = el.querySelector('#ui-dialog-input');
            input.classList.add('d-none');
            const okBtn = el.querySelector('#ui-dialog-ok');
            okBtn.className = `btn ${opts.danger ? 'btn-danger' : 'btn-primary'}`;
            okBtn.textContent = opts.okText || 'OK';

            const modal = bootstrap.Modal.getOrCreateInstance(el);
            // Resolve only after the modal has FULLY closed (hidden.bs.modal), not on click -
            // otherwise a caller chaining a second uiConfirm/uiPrompt right after this one
            // resolves would call modal.show() again while this modal is still mid-hide
            // animation on the same shared element, which leaves Bootstrap's modal/backdrop
            // state stuck (the actual bug behind "the second confirm dialog never appears").
            let outcome = false;
            const onOk = () => { outcome = true; modal.hide(); };
            const onHidden = () => { cleanup(); resolve(outcome); };
            function cleanup() {
                okBtn.removeEventListener('click', onOk);
                el.removeEventListener('hidden.bs.modal', onHidden);
            }
            okBtn.addEventListener('click', onOk);
            el.addEventListener('hidden.bs.modal', onHidden, { once: true });
            modal.show();
        });
    }

    // Promise-based replacement for prompt(). Resolves to the entered string, or null if cancelled.
    function uiPrompt(message, defaultValue, opts) {
        opts = opts || {};
        return new Promise((resolve) => {
            const el = ensureDialogModal();
            el.querySelector('#ui-dialog-title').textContent = opts.title || 'Input required';
            el.querySelector('#ui-dialog-message').textContent = message;
            const input = el.querySelector('#ui-dialog-input');
            input.classList.remove('d-none');
            input.value = defaultValue ?? '';
            input.type = opts.type || 'text';
            const okBtn = el.querySelector('#ui-dialog-ok');
            okBtn.className = 'btn btn-primary';
            okBtn.textContent = opts.okText || 'OK';

            const modal = bootstrap.Modal.getOrCreateInstance(el);
            // Same resolve-after-fully-hidden fix as uiConfirm above.
            let outcome = null;
            const onOk = () => { outcome = input.value; modal.hide(); };
            const onHidden = () => { cleanup(); resolve(outcome); };
            const onKeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); onOk(); } };
            function cleanup() {
                okBtn.removeEventListener('click', onOk);
                input.removeEventListener('keydown', onKeydown);
                el.removeEventListener('hidden.bs.modal', onHidden);
            }
            okBtn.addEventListener('click', onOk);
            input.addEventListener('keydown', onKeydown);
            el.addEventListener('hidden.bs.modal', onHidden, { once: true });
            modal.show();
            setTimeout(() => input.focus(), 200);
        });
    }

    function inferAlertVariant(message) {
        const value = String(message || '').toLowerCase();
        if (/saved|created|updated|changed|reopened|reset to|imported|added|deleted|removed|successful|completed|bid placed/.test(value)) return 'success';
        if (/missing|select |enter |no player|locked|required|too large|unable|could not|permission|must |cannot/.test(value)) return 'warning';
        return 'error';
    }

    // Transparent upgrade for legacy alert() calls, with semantics inferred from the message.
    window.alert = (msg) => notify(msg, inferAlertVariant(msg));
    window.uiConfirm = uiConfirm;
    window.uiPrompt = uiPrompt;
    window.notify = notify;
    window.showUiNotice = notify;
    window.clearUiNotices = clearNotices;
})();
