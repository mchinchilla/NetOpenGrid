/* NetOpenGrid: dark mode before the first paint (no flash). Loaded without defer at the top of the grid document, as a
   file instead of an inline script, so a strict Content-Security-Policy (script-src 'self') allows it. */
(() => {
  try {
    const stored = localStorage.getItem('netgrid:theme');
    const dark = stored ? stored === 'dark' : window.matchMedia('(prefers-color-scheme: dark)').matches;
    if (dark) document.documentElement.classList.add('dark');
  } catch {
    /* storage unavailable: the system preference was already applied or the page stays light */
  }
})();
