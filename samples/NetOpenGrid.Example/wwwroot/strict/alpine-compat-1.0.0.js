/*
 * alpine-compat 1.0.0 — extensión propia de Tekium Call Accounting (MIT).
 * Une htmx 2 y Alpine 3 (build CSP): antes de que htmx limpie o reemplace un elemento, destruye su
 * árbol de Alpine para que efectos, listeners globales, x-teleport y gráficas no queden huérfanos.
 * Uso: hx-ext="alpine-compat" en <html>. No quitarla ni reemplazarla (Rules R15).
 */
(function () {
  'use strict';

  function destroyTree(element) {
    if (window.Alpine && element && element.nodeType === Node.ELEMENT_NODE) {
      window.Alpine.destroyTree(element);
    }
  }

  htmx.defineExtension('alpine-compat', {
    onEvent: function (name, event) {
      if (name === 'htmx:beforeCleanupElement') {
        destroyTree(event.detail.elt);
      }

      return true;
    }
  });
})();
