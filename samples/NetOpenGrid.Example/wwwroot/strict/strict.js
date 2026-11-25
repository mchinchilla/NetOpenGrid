/* Strict-CSP host page: loaded first and without defer, so it sees every CSP violation while the page parses. Everything
   that goes wrong (CSP violations, Alpine expression errors, htmx errors) is also kept in window.__strict for inspection. */
window.__strict = [];
const keep = (kind, text) => { window.__strict.push(`${kind}: ${text}`); };
document.addEventListener('securitypolicyviolation', (e) => {
  keep('csp', `${e.effectiveDirective} ${e.blockedURI || 'inline'} ${e.sample ? '«' + e.sample.slice(0, 100) + '»' : ''} ${e.sourceFile ? e.sourceFile.split('/').pop() + ':' + e.lineNumber : ''}`);
});
for (const level of ['warn', 'error']) {
  const original = console[level].bind(console);
  console[level] = (...args) => { keep(level, args.map((a) => (a instanceof Element ? a.outerHTML.slice(0, 120) : String(a))).join(' ').slice(0, 300)); original(...args); };
}
window.addEventListener('error', (e) => keep('exception', `${e.message} ${e.filename ? e.filename.split('/').pop() + ':' + e.lineno : ''}`));
document.addEventListener('netgrid:bulk-action', (e) => { keep('event', `bulk-action ${JSON.stringify(e.detail)}`); });
document.addEventListener('netgrid:row-action', (e) => { keep('event', `row-action ${JSON.stringify(e.detail)}`); });
