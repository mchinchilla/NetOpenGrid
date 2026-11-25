/* Strict-CSP host page: htmx eval off (no hx-on, no js: in hx-vals), as a host with script-src 'self' runs it. */
document.addEventListener('netgrid:bulk-action', (e) => { console.log('[strict] bulk-action', e.detail); });
document.addEventListener('netgrid:row-action', (e) => { console.log('[strict] row-action', e.detail); });
