/* NetOpenGrid client runtime: Alpine.js component + HTMX wiring. ES2020, async/await only. */
(() => {
  'use strict';

  const THEME_KEY = 'netgrid:theme';
  const MAX_SEARCH = 200;
  const PREFIX = () => window.__NETGRID__?.prefix || '/netgrid';

  const isEmptyOp = (op) => op === 'is-empty' || op === 'is-not-empty';

  const splitFilterTokens = (raw) => {
    if (!raw.includes('[')) return raw.split(',');
    const tokens = [];
    let depth = 0;
    let start = 0;
    for (let i = 0; i < raw.length; i++) {
      const c = raw[i];
      if (c === '[') depth++;
      else if (c === ']') depth--;
      else if (c === ',' && depth === 0) {
        tokens.push(raw.slice(start, i).trim());
        start = i + 1;
      }
    }
    tokens.push(raw.slice(start).trim());
    return tokens.filter((t) => t.length > 0);
  };

  const FALLBACK = {
    'records.one': '1 record',
    'records.many': '{n} records',
    'range.empty': 'No records',
    'range.format': '{from}-{to} of {total}',
    'select.selected.one': '1 selected',
    'select.selected.many': '{n} selected',
    'chips.anyOf': 'any of {values}',
    'chips.anyOfMore': 'any of {values} +{n}',
    'filter.values.truncated': 'Showing {shown} of {total} values',
    'ops.equals': 'equals',
    'ops.not-equals': 'not equals',
    'ops.contains': 'contains',
    'ops.starts-with': 'starts with',
    'ops.ends-with': 'ends with',
    'ops.gt': 'greater than',
    'ops.gte': 'greater or equal',
    'ops.lt': 'less than',
    'ops.lte': 'less or equal',
    'ops.is-empty': 'is empty',
    'ops.is-not-empty': 'is not empty'
  };

  const L = (key, vars) => {
    let out = window.__NETGRID__?.locale?.[key] || FALLBACK[key] || key;
    if (vars) {
      for (const [k, v] of Object.entries(vars)) {
        out = out.replaceAll(`{${k}}`, String(v));
      }
    }
    return out;
  };

  const parseInValues = (json) => {
    try {
      const parsed = JSON.parse(json);
      return Array.isArray(parsed) ? parsed.map(String) : [];
    } catch {
      return [];
    }
  };

  function toParams(state, excludeField) {
    const params = new URLSearchParams();
    if (state.page > 1) params.set('page', String(state.page));
    if (!state.pageSizeLocked) params.set('pageSize', String(state.pageSize));
    for (const sort of state.sorts) {
      params.append('sort', `${sort.field}:${sort.dir}`);
    }
    for (const [field, filter] of Object.entries(state.filters)) {
      if (!filter || field === excludeField) continue;
      if (isEmptyOp(filter.op)) {
        params.append('filter', `${field}:${filter.op}`);
      } else {
        params.append('filter', `${field}:${filter.op}:${filter.value}`);
      }
    }
    if (state.q) params.set('q', state.q.slice(0, MAX_SEARCH));
    if (state.groupBy.length) params.set('groupby', state.groupBy.join(','));
    if (state.expanded.length) params.set('expand', state.expanded.join(','));
    // Pinning moves columns to the edges, so the order sent is the effective one; it is only sent
    // when it differs from the server's default (or the user reordered columns).
    const order = state.effectiveColumnOrder?.() ?? state.columnOrder ?? [];
    if (order.length && (state.columnOrder?.length || order.join(',') !== (state.defaultColumnOrder ?? []).join(','))) {
      params.set('cols', order.join(','));
    }
    if (state.hiddenColumns?.length) params.set('hide', state.hiddenColumns.join(','));
    return params;
  }

  // Una request en vuelo por grid. Cancelarla con fetch es silencioso; con htmx cada abort
  // se registraba como console.error (htmx:sendAbort), y sin `source` todos los grids de la
  // página compartían la cola de <body> y la request pendiente de uno descartaba la de otro.
  const inflight = new Map();

  // Virtual-scroll bookkeeping per grid, kept outside Alpine (large HTML strings and a Map of
  // blocks do not need, and should not pay for, reactivity).
  const virtualStore = new Map();

  // requestAnimationFrame never fires in a hidden tab (a grid opened in the background, a test
  // runner): run on the next frame or after 50 ms, whichever comes first, and only once.
  const nextFrame = (fn) => {
    let done = false;
    const run = () => {
      if (done) return;
      done = true;
      fn();
    };
    requestAnimationFrame(run);
    setTimeout(run, 50);
  };

  async function fetchRows(state) {
    if (typeof htmx === 'undefined') {
      throw new Error('NetOpenGrid: htmx is not loaded.');
    }

    if (state.isVirtual?.()) {
      // Virtual scroll: every query change starts over from block 0 at the top.
      state.page = 1;
      state.pageSize = state.virtualCfg.blockSize;
    }

    const params = toParams(state);
    const query = params.toString();
    window.history.replaceState(null, '', `${window.location.pathname}${query ? '?' + query : ''}`);

    const body = document.getElementById(`${state.id}-body`);
    if (!body) return;

    inflight.get(state.id)?.abort();
    const controller = new AbortController();
    inflight.set(state.id, controller);

    state.loading = true;
    try {
      const response = await fetch(`${PREFIX()}/${state.id}/rows${query ? '?' + query : ''}`, {
        headers: { 'HX-Request': 'true' },
        signal: controller.signal
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const html = await response.text();
      if (controller.signal.aborted) return;

      // The swap replaces every cell, focused one included: note it first so the keyboard
      // user lands back on the same row/column of the new rows.
      const refocus = body.contains(document.activeElement);
      htmx.swap(body, html, { swapStyle: 'innerHTML' });
      state.applyRowsMeta(response.headers);
      if (state.isVirtual?.()) state.virtualReset(body);
      state.syncRoving(refocus);
    } catch (error) {
      if (error?.name !== 'AbortError') console.error('[netgrid] row fetch failed:', error);
    } finally {
      if (inflight.get(state.id) === controller) {
        inflight.delete(state.id);
        state.loading = false;
      }
    }
  }

  document.addEventListener('alpine:init', () => {
    Alpine.data('netgrid', (id) => ({
      id,
      q: '',
      page: 1,
      pageSize: 25,
      pageSizeLocked: true,
      sorts: [],
      filters: {},
      selected: [],
      meta: { total: 0, page: 1, pageSize: 25, pages: 1 },
      loading: false,
      cursorRow: 0,
      cursorCol: 0,
      columnWidths: {},
      resizing: false,
      hiddenColumns: [],
      columnsMenu: false,
      exportMenu: false,
      viewsMenu: false,
      groupDropActive: false,
      pinnedRightFields: [],
      virtualCfg: null,
      cursorAbs: null,
      pendingFocusRow: null,
      dragGroupField: null,
      serverViews: [],
      userViews: [],
      viewName: '',
      defaultColumnOrder: [],
      editing: null,
      editingOp: 'equals',
      editingValue: '',
      popoverFlip: {},
      listItems: {},
      listMeta: {},
      listSearch: '',
      columnOrder: null,
      pinnedFields: [],
      dragField: null,
      groupBy: [],
      expanded: [],

      get totalPages() {
        return Math.max(1, this.meta.pages);
      },

      get rangeLabel() {
        if (this.meta.total === 0) return L('range.empty');
        const from = (this.page - 1) * this.pageSize + 1;
        const to = Math.min(this.page * this.pageSize, this.meta.total);
        return L('range.format', { from, to, total: this.meta.total });
      },

      get recordsLabel() {
        return this.meta.total === 1
          ? L('records.one')
          : L('records.many', { n: this.meta.total });
      },

      get selectedLabel() {
        return this.selected.length === 1
          ? L('select.selected.one')
          : L('select.selected.many', { n: this.selected.length });
      },

      get activeFilters() {
        return Object.entries(this.filters)
          .filter(([, f]) => f)
          .map(([field, f]) => ({
            field,
            label: `${this.columnHeader(field)} ${this.filterLabel(f)}`
          }));
      },

      filterLabel(f) {
        if (f.op === 'in') {
          const values = parseInValues(f.value);
          const shown = values.slice(0, 2).join(', ');
          return values.length > 2
            ? L('chips.anyOfMore', { values: shown, n: values.length - 2 })
            : L('chips.anyOf', { values: shown });
        }

        const opText = L(`ops.${f.op}`);
        return `${opText}${isEmptyOp(f.op) ? '' : ` ${f.value}`}`;
      },

      get allPageSelected() {
        const ids = this.currentPageIds();
        return ids.length > 0 && ids.every((key) => this.selected.includes(key));
      },

      get editingLabel() {
        return this.editing === null ? '' : this.columnHeader(this.editing);
      },

      columnHeader(field) {
        return this.columnInfo(field)?.header || field;
      },

      columnInfo(field) {
        const columns = window.__NETGRID__?.columns?.[id] || [];
        return columns.find((c) => c.field === field);
      },

      init() {
        const initial = window.__NETGRID__?.initial?.[id];
        if (initial) {
          this.meta = { ...initial };
          this.page = initial.page ?? 1;
          this.pageSize = initial.pageSize ?? 25;
        }

        this.pageSizeLocked = false;
        this.loadColumnOrder();
        this.virtualCfg = window.__NETGRID__?.virtual?.[id] ?? null;
        this.serverViews = window.__NETGRID__?.views?.[id] ?? [];
        this.defaultColumnOrder = (window.__NETGRID__?.columns?.[id] ?? []).map((c) => c.field);
        this.loadUserViews();
        this.loadHiddenColumns();
        this.seedFromUrl();
        this.applyHeaderVisibility();
        this.loadPinnedState();
        this.applyPinStateAll();
        this.syncRoving(false);
        this.loadColumnWidths();

        window.addEventListener('resize', () => this.applyPinnedOffsets());

        if (this.isVirtual()) {
          // The server's first render is block 0 only when it used the block size on page 1
          // (a link or saved view may carry another pageSize): otherwise ask for block 0.
          const body = document.getElementById(`${this.id}-body`);
          if (this.page === 1 && this.meta.pageSize === this.virtualCfg.blockSize) {
            if (body) nextFrame(() => this.virtualReset(body));
          } else {
            this.refresh();
          }
        }

        // The host page asks for fresh rows (e.g. after handling a row action):
        // document.dispatchEvent(new CustomEvent('netgrid:refresh', { detail: { grid: 'id' } })),
        // or from a same-origin parent: frame.contentWindow.postMessage({ type: 'netgrid:refresh', grid: 'id' }, origin).
        // A drag that ends outside any drop target must not leave a stale source behind.
        document.addEventListener('dragend', () => {
          this.dragField = null;
          this.dragGroupField = null;
          this.groupDropActive = false;
        });

        const refreshIfMine = (grid) => { if (!grid || grid === this.id) this.refresh(); };
        document.addEventListener('netgrid:refresh', (e) => refreshIfMine(e.detail?.grid));
        window.addEventListener('message', (e) => {
          if (e.origin === window.location.origin && e.data?.type === 'netgrid:refresh') refreshIfMine(e.data.grid);
        });
        nextFrame(() => this.applyPinnedOffsets());

        // The shell server-renders flat rows in the default column order. A saved/linked column
        // order or a groupby= deep link needs one refresh to show the requested view.
        if (this.columnOrder?.length || this.groupBy.length || this.hiddenColumns.length ||
            this.effectiveColumnOrder().join(',') !== this.defaultColumnOrder.join(',')) {
          this.applyHeaderOrder();
          this.refresh();
        }
      },

      loadColumnOrder() {
        try {
          const saved = localStorage.getItem(`netgrid:cols:${id}`);
          if (saved) this.columnOrder = JSON.parse(saved);
        } catch {
          this.columnOrder = null;
        }
      },

      // ---- Pinning: left, right or none ------------------------------------------------------
      // Stored explicitly as { left, right } so a column the server pins can be unpinned too.
      // The older format (a plain array of left pins) is still read.

      loadPinnedState() {
        const columns = window.__NETGRID__?.columns?.[id] || [];
        const serverLeft = columns.filter((c) => c.pin === true || c.pin === 'left').map((c) => c.field);
        const serverRight = columns.filter((c) => c.pin === 'right').map((c) => c.field);
        const known = new Set(columns.map((c) => c.field));

        let saved = null;
        try {
          saved = JSON.parse(localStorage.getItem(`netgrid:pins:${id}`) || 'null');
        } catch {
          saved = null;
        }

        if (Array.isArray(saved)) {
          this.pinnedFields = [...new Set([...serverLeft, ...saved])];
          this.pinnedRightFields = serverRight.filter((f) => !this.pinnedFields.includes(f));
        } else if (saved && typeof saved === 'object') {
          this.pinnedFields = (saved.left || []).filter((f) => known.has(f));
          this.pinnedRightFields = (saved.right || []).filter((f) => known.has(f) && !this.pinnedFields.includes(f));
        } else {
          this.pinnedFields = serverLeft;
          this.pinnedRightFields = serverRight;
        }
      },

      savePins() {
        try {
          localStorage.setItem(`netgrid:pins:${this.id}`, JSON.stringify({ left: this.pinnedFields, right: this.pinnedRightFields }));
        } catch {
          /* storage unavailable; pins stay session-only */
        }
      },

      pinSide(field) {
        if (this.pinnedFields.includes(field)) return 'left';
        if (this.pinnedRightFields.includes(field)) return 'right';
        return '';
      },

      isPinned(field) {
        return this.pinSide(field) !== '';
      },

      pinButtonClass(field) {
        const side = this.pinSide(field);
        if (side === 'left') return 'text-brand-600 dark:text-brand-400';
        if (side === 'right') return 'text-brand-600 dark:text-brand-400 -scale-x-100';
        return 'text-neutral-300 hover:text-neutral-500 dark:text-neutral-600 dark:hover:text-neutral-400';
      },

      pinLabel(field) {
        const side = this.pinSide(field);
        const key = side === '' ? 'pin.left.aria' : side === 'left' ? 'pin.right.aria' : 'pin.none.aria';
        return L(key, { field: this.columnInfo(field)?.header || field });
      },

      // Left-pinned first, then the rest, then right-pinned: sticky needs pins at their edge.
      effectiveColumnOrder() {
        const base = this.columnOrder?.length ? this.columnOrder : this.defaultColumnOrder;
        const left = base.filter((f) => this.pinnedFields.includes(f));
        const right = base.filter((f) => this.pinnedRightFields.includes(f));
        const middle = base.filter((f) => !left.includes(f) && !right.includes(f));
        return [...left, ...middle, ...right];
      },

      // Never this.$el: inside a method called from an event handler, Alpine's $el is the element
      // the listener sits on (a <th>, a pin button, the <tbody>), not the grid's root.
      root() {
        return document.getElementById(this.id) ?? document.createElement('div');
      },

      // The rows endpoint only swaps the <tbody>, so a new column order must be applied to the
      // header cells here (after a drop, and on load when the order comes from localStorage).
      applyHeaderOrder() {
        const headerRow = this.root().querySelector('thead tr');
        const order = this.effectiveColumnOrder();
        if (!headerRow || !order?.length) return;
        for (const field of order) {
          const th = headerRow.querySelector(`th[data-field="${CSS.escape(field)}"]`);
          if (th) headerRow.appendChild(th);
        }

        // The actions column has no field: keep it last, after the reordered data columns.
        const actions = headerRow.querySelector('th[data-actions]');
        if (actions) headerRow.appendChild(actions);
      },

      // ---- Saved views --------------------------------------------------------------------
      // A view is the grid's own query string. Applying one resets every piece of view state
      // first, so nothing from the previous view (a filter, a hidden column) leaks into it.
      // Page and page size never decide which view is active.

      loadUserViews() {
        try {
          const saved = JSON.parse(localStorage.getItem(`netgrid:views:${this.id}`) || '[]');
          this.userViews = Array.isArray(saved)
            ? saved.filter((v) => v && typeof v.name === 'string' && typeof v.query === 'string')
            : [];
        } catch {
          this.userViews = [];
        }
      },

      saveUserViews() {
        try {
          localStorage.setItem(`netgrid:views:${this.id}`, JSON.stringify(this.userViews));
        } catch {
          /* storage unavailable; views last for this page only */
        }
      },

      normalizeQuery(query) {
        const params = new URLSearchParams(query);
        params.delete('page');
        params.delete('pageSize');
        params.sort();
        return params.toString();
      },

      currentQuery() {
        return this.normalizeQuery(toParams(this, null).toString());
      },

      isActiveView(query) {
        return this.normalizeQuery(query) === this.currentQuery();
      },

      activeViewName() {
        const current = this.currentQuery();
        if (current === '') return '';
        const match = [...this.userViews, ...this.serverViews].find((v) => this.normalizeQuery(v.query) === current);
        return match ? match.name : '';
      },

      async applyView(query) {
        this.sorts = [];
        this.filters = {};
        this.q = '';
        this.groupBy = [];
        this.expanded = [];
        this.columnOrder = null;
        this.hiddenColumns = [];

        this.seedFromParams(new URLSearchParams(query));
        this.page = 1;
        this.viewsMenu = false;

        this.applyHeaderOrder();
        this.applyHeaderVisibility();
        this.saveHiddenColumns();
        try {
          if (this.columnOrder?.length) localStorage.setItem(`netgrid:cols:${this.id}`, JSON.stringify(this.columnOrder));
          else localStorage.removeItem(`netgrid:cols:${this.id}`);
        } catch {
          /* storage unavailable */
        }

        await this.refresh();
      },

      saveCurrentView() {
        const name = this.viewName.trim().slice(0, 60);
        if (!name) return;

        const params = toParams(this, null);
        params.delete('page');
        const view = { name, query: params.toString() };
        const same = (v) => v.name.toLocaleLowerCase() === name.toLocaleLowerCase();
        this.userViews = this.userViews.some(same)
          ? this.userViews.map((v) => (same(v) ? view : v))
          : [...this.userViews, view];

        this.saveUserViews();
        this.viewName = '';
      },

      deleteView(name) {
        this.userViews = this.userViews.filter((v) => v.name !== name);
        this.saveUserViews();
      },

      deleteViewLabel(name) {
        return L('views.delete.aria', { name });
      },

      // ---- Show/hide columns ---------------------------------------------------------------
      // The server leaves hidden columns out of the rows (hide=...), so colspans, totals and the
      // CSV export stay consistent; the header keeps every <th> and only toggles its hidden flag.

      loadHiddenColumns() {
        try {
          const saved = JSON.parse(localStorage.getItem(`netgrid:hidden:${this.id}`) || '[]');
          if (Array.isArray(saved)) this.hiddenColumns = saved.map(String);
        } catch {
          this.hiddenColumns = [];
        }
      },

      saveHiddenColumns() {
        try {
          localStorage.setItem(`netgrid:hidden:${this.id}`, JSON.stringify(this.hiddenColumns));
        } catch {
          /* storage unavailable; the choice stays in the URL only */
        }
      },

      allColumnFields() {
        return Array.from(this.root().querySelectorAll('thead th[data-field]'), (th) => th.dataset.field);
      },

      isHidden(field) {
        return this.hiddenColumns.includes(field);
      },

      canToggleColumn(field) {
        if (this.isHidden(field)) return true;
        return this.allColumnFields().filter((f) => !this.isHidden(f)).length > 1;
      },

      applyHeaderVisibility() {
        this.root().querySelectorAll('thead th[data-field]').forEach((th) => {
          th.hidden = this.isHidden(th.dataset.field);
        });
      },

      async toggleColumn(field) {
        if (!this.canToggleColumn(field)) return;
        this.hiddenColumns = this.isHidden(field)
          ? this.hiddenColumns.filter((f) => f !== field)
          : [...this.hiddenColumns, field];
        this.saveHiddenColumns();
        this.applyHeaderVisibility();
        await this.refresh();
      },

      async showAllColumns() {
        if (this.hiddenColumns.length === 0) return;
        this.hiddenColumns = [];
        this.saveHiddenColumns();
        this.applyHeaderVisibility();
        await this.refresh();
      },

      async togglePin(field) {
        const side = this.pinSide(field);
        const before = this.effectiveColumnOrder().join(',');

        this.pinnedFields = this.pinnedFields.filter((f) => f !== field);
        this.pinnedRightFields = this.pinnedRightFields.filter((f) => f !== field);
        if (side === '') this.pinnedFields.push(field);
        else if (side === 'left') this.pinnedRightFields.push(field);

        this.savePins();
        this.applyHeaderOrder();
        this.applyPinStateAll();

        // The column moved to (or away from) an edge: the rows must follow the header.
        if (this.effectiveColumnOrder().join(',') !== before) await this.refresh();
        else this.applyPinnedOffsets();
      },

      applyPinStateAll() {
        const fields = new Set(Array.from(this.root().querySelectorAll('thead th[data-field]'), (th) => th.dataset.field));
        for (const field of fields) {
          this.applyPinState(field, this.pinSide(field));
        }
      },

      applyPinState(field, side) {
        const bg = 'dark:bg-[color:color-mix(in_oklab,var(--color-neutral-800)_60%,var(--color-neutral-900))]';
        const th = ['sticky', 'z-30', 'border-neutral-200', 'bg-neutral-50', 'dark:border-neutral-800', bg];
        const td = ['sticky', 'z-10', 'border-neutral-100', 'bg-white', 'hover:bg-brand-50/40', 'dark:border-neutral-800/60', 'dark:bg-neutral-900', 'dark:hover:bg-white/[0.04]'];

        this.root().querySelectorAll(`th[data-field="${CSS.escape(field)}"], td[data-field="${CSS.escape(field)}"]`).forEach((el) => {
          const classes = el.tagName === 'TH' ? th : td;
          el.classList.remove(...classes, 'border-r', 'border-l');
          el.removeAttribute('data-pin');
          el.removeAttribute('data-pin-right');
          el.style.left = '';
          el.style.right = '';

          if (side === 'left') {
            el.classList.add(...classes, 'border-r');
            el.setAttribute('data-pin', field);
          } else if (side === 'right') {
            el.classList.add(...classes, 'border-l');
            el.setAttribute('data-pin-right', field);
          }
        });
      },

      // Offsets are the summed widths of the pinned cells before (left) or after (right) each one
      // in its row. Never offsetLeft: on a sticky cell it already includes the stuck shift, so a
      // table scrolled (or overflowing, for right pins) would get offsets off by the scroll amount.
      applyPinnedOffsets() {
        this.root().querySelectorAll('table tr').forEach((row) => {
          const cells = Array.from(row.cells).filter((cell) => cell.getClientRects().length > 0);

          let left = 0;
          for (const cell of cells) {
            if (!cell.hasAttribute('data-pin')) continue;
            cell.style.left = `${left}px`;
            left += cell.offsetWidth;
          }

          let right = 0;
          for (const cell of cells.reverse()) {
            if (!cell.hasAttribute('data-pin-right')) continue;
            cell.style.right = `${right}px`;
            right += cell.offsetWidth;
          }
        });
      },

      // ---- Column resizing --------------------------------------------------------------------
      // First resize freezes every column at its current width and switches the table to
      // table-layout:fixed, so nothing jumps; from then on each column has an exact width and
      // overflowing cells end in an ellipsis. Widths persist per grid in localStorage.

      loadColumnWidths() {
        try {
          const saved = JSON.parse(localStorage.getItem(`netgrid:widths:${this.id}`) || '{}');
          if (saved && typeof saved === 'object') this.columnWidths = saved;
        } catch {
          this.columnWidths = {};
        }

        if (Object.keys(this.columnWidths).length) this.applyColumnWidths();
      },

      saveColumnWidths() {
        try {
          localStorage.setItem(`netgrid:widths:${this.id}`, JSON.stringify(this.columnWidths));
        } catch {
          /* storage unavailable; widths stay session-only */
        }
      },

      headerCells() {
        return Array.from(this.root().querySelectorAll('thead tr > th')).filter((th) => th.getClientRects().length > 0);
      },

      applyColumnWidths() {
        const table = this.root().querySelector('table');
        if (!table) return;

        // Measure before switching layouts: columns without a stored width keep their natural one.
        const cells = this.headerCells();
        const widths = cells.map((th) => {
          const field = th.dataset.field;
          if (field && !(field in this.columnWidths)) this.columnWidths[field] = th.offsetWidth;
          return field ? this.columnWidths[field] : th.offsetWidth;
        });

        cells.forEach((th, i) => { th.style.width = `${widths[i]}px`; });
        table.style.tableLayout = 'fixed';
        table.style.width = `${widths.reduce((sum, w) => sum + w, 0)}px`;
        table.style.minWidth = '0';
        table.classList.add('is-resized');
        this.applyPinnedOffsets();
      },

      minColumnWidth(field) {
        const th = this.root().querySelector(`thead th[data-field="${CSS.escape(field)}"]`);
        const controls = th?.querySelector(':scope > div');
        return Math.max(48, (controls?.scrollWidth ?? 0) + 32);   // px-4 on both sides
      },

      setColumnWidth(field, width, save = true) {
        this.columnWidths[field] = Math.max(this.minColumnWidth(field), Math.round(width));
        this.applyColumnWidths();
        if (save) this.saveColumnWidths();
      },

      startResize(field, event) {
        if (event.button !== 0) return;
        event.preventDefault();

        const handle = event.currentTarget;
        const th = handle.closest('th');
        const startX = event.clientX;
        const startWidth = this.columnWidths[field] ?? th.offsetWidth;
        this.resizing = true;
        handle.setPointerCapture?.(event.pointerId);

        const move = (e) => this.setColumnWidth(field, startWidth + e.clientX - startX, false);
        const end = () => {
          handle.removeEventListener('pointermove', move);
          handle.removeEventListener('pointerup', end);
          handle.removeEventListener('pointercancel', end);
          this.resizing = false;
          this.saveColumnWidths();
        };

        handle.addEventListener('pointermove', move);
        handle.addEventListener('pointerup', end);
        handle.addEventListener('pointercancel', end);
      },

      // Double-click on the handle: fit the widest cell of the current page (and the header).
      autoFitColumn(field) {
        const table = this.root().querySelector('table');
        if (!table) return;
        if (!table.classList.contains('is-resized')) this.applyColumnWidths();

        let widest = 0;
        table.querySelectorAll(`tbody td[data-field="${CSS.escape(field)}"]`).forEach((td) => {
          if (td.getClientRects().length) widest = Math.max(widest, td.scrollWidth);
        });

        this.setColumnWidth(field, widest + 2);
      },

      onColumnDragStart(field, event) {
        if (this.resizing) {
          event.preventDefault();
          return;
        }

        this.dragField = field;
        this.dragGroupField = null;
        event.dataTransfer?.setData('text/plain', field);
        event.dataTransfer && (event.dataTransfer.effectAllowed = 'move');
      },

      async onColumnDrop(field, event) {
        // A group chip dropped on a header is not a column move.
        if (this.dragGroupField || event.dataTransfer?.getData('text/plain')?.startsWith('netgrid-group:')) {
          this.dragGroupField = null;
          return;
        }

        const dragged = this.dragField || event.dataTransfer?.getData('text/plain');
        this.dragField = null;
        if (!dragged || dragged === field) return;

        const order = Array.from(
          this.root().querySelectorAll('th[data-field]'),
          (th) => th.dataset.field
        );
        const from = order.indexOf(dragged);
        const to = order.indexOf(field);
        if (from === -1 || to === -1) return;

        order.splice(to, 0, order.splice(from, 1)[0]);
        this.columnOrder = order;
        this.applyHeaderOrder();

        try {
          localStorage.setItem(`netgrid:cols:${id}`, JSON.stringify(order));
        } catch {
          /* storage unavailable; order stays session-only */
        }

        await this.refresh();
      },

      seedFromUrl() {
        this.seedFromParams(new URLSearchParams(window.location.search));
      },

      seedFromParams(params) {

        const page = parseInt(params.get('page'), 10);
        if (Number.isFinite(page) && page >= 1) this.page = page;

        const pageSize = parseInt(params.get('pageSize'), 10);
        if (Number.isFinite(pageSize) && pageSize >= 1) this.pageSize = pageSize;

        this.sorts = params.getAll('sort')
          .flatMap((value) => value.split(','))
          .map((token) => {
            const i = token.lastIndexOf(':');
            const field = i < 0 ? token : token.slice(0, i);
            const dir = i >= 0 && token.slice(i + 1).toLowerCase() === 'desc' ? 'desc' : 'asc';
            return { field, dir };
          })
          .filter((sort) => sort.field);

        for (const raw of params.getAll('filter')) {
          for (const rawToken of splitFilterTokens(raw)) {
            const parts = rawToken.split(':');
            if (parts.length < 2) continue;
            const [field, op] = parts;
            const value = parts.length >= 3 ? parts.slice(2).join(':') : '';
            this.filters[field] = { op, value };
          }
        }

        const q = params.get('q');
        if (q) this.q = q;

        const cols = params.get('cols');
        if (cols) {
          const fields = cols.split(',').map((s) => s.trim()).filter(Boolean);
          if (fields.length > 0) this.columnOrder = fields;
        }

        const hide = params.get('hide');
        if (hide !== null) {
          this.hiddenColumns = hide.split(',').map((s) => s.trim()).filter(Boolean);
        }

        this.groupBy = params.get('groupby')
          ? params.get('groupby').split(',').map((s) => s.trim()).filter(Boolean)
          : [];

        this.expanded = params.get('expand')
          ? params.get('expand').split(',').map((s) => s.trim()).filter(Boolean)
          : [];
      },

      toggleGroup(path) {
        const index = this.expanded.indexOf(path);
        if (index === -1) {
          this.expanded.push(path);
        } else {
          this.expanded.splice(index, 1);
        }

        return this.refresh();
      },

      addGroupBy(field) {
        if (!field || this.groupBy.includes(field) || this.groupBy.length >= 3) return;
        this.groupBy.push(field);
        this.expanded = [];
        this.page = 1;
        return this.refresh();
      },

      // ---- Group panel: drag headers in, drag chips to reorder ------------------------------

      onGroupPanelDragOver(event) {
        if (!this.dragField && !this.dragGroupField) return;
        this.groupDropActive = true;
        if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
      },

      async onGroupPanelDrop(event) {
        this.groupDropActive = false;
        const chip = this.dragGroupField;
        const column = this.dragField || event.dataTransfer?.getData('text/plain');
        this.dragGroupField = null;
        this.dragField = null;

        if (chip) {
          await this.moveGroupLevel(chip, this.groupBy.length);   // dropped on the panel: last level
          return;
        }

        if (column && !column.startsWith('netgrid-group:') && this.columnInfo(column)) {
          await this.addGroupBy(column);
        }
      },

      onGroupChipDragStart(field, event) {
        this.dragGroupField = field;
        this.dragField = null;
        event.dataTransfer?.setData('text/plain', `netgrid-group:${field}`);
        event.dataTransfer && (event.dataTransfer.effectAllowed = 'move');
      },

      async onGroupChipDrop(target, event) {
        this.groupDropActive = false;
        const chip = this.dragGroupField;
        const column = this.dragField;
        this.dragGroupField = null;
        this.dragField = null;

        if (chip) {
          await this.moveGroupLevel(chip, this.groupBy.indexOf(target));
        } else if (column && this.columnInfo(column)) {
          await this.addGroupBy(column);
        }
      },

      async moveGroupLevel(field, to) {
        const from = this.groupBy.indexOf(field);
        if (from === -1 || from === to) return;

        const next = [...this.groupBy];
        next.splice(from, 1);
        next.splice(Math.min(to, next.length), 0, field);
        if (next.join(',') === this.groupBy.join(',')) return;

        this.groupBy = next;
        this.expanded = [];                     // group paths change with the level order
        this.page = 1;
        await this.refresh();
      },

      removeGroupLabel(field) {
        return L('group.remove.aria', { field: this.columnInfo(field)?.header || field });
      },

      removeGroupBy(field) {
        this.groupBy = this.groupBy.filter((f) => f !== field);
        this.expanded = [];
        this.page = 1;
        return this.refresh();
      },

      applyRowsMeta(headers) {
        const total = headers.get('X-Grid-Total');
        if (total !== null) {
          this.meta = {
            total: parseInt(total, 10) || 0,
            page: parseInt(headers.get('X-Grid-Page'), 10) || this.page,
            pageSize: parseInt(headers.get('X-Grid-Page-Size'), 10) || this.pageSize,
            pages: Math.max(1, parseInt(headers.get('X-Grid-Page-Count'), 10) || 1)
          };
          this.page = this.meta.page;
          this.pageSize = this.meta.pageSize;
        }

        this.applyPinStateAll();
        if (Object.keys(this.columnWidths).length) this.applyColumnWidths();
        nextFrame(() => this.applyPinnedOffsets());
      },

      // Downloads the whole filtered set (every page) in the current view: sort, filters,
      // column order and hidden columns all travel in the query.
      exportAs(format) {
        const params = toParams(this, null);
        params.delete('page');
        if (format && format !== 'csv') params.set('format', format);
        window.location.href = `${PREFIX()}/${this.id}/export?${params.toString()}`;
      },

      exportCsv() {
        this.exportAs('csv');
      },

      async refresh() {
        await fetchRows(this);
      },

      async sortBy(field, event) {
        const index = this.sorts.findIndex((s) => s.field === field);
        if (index === -1) {
          if (event?.shiftKey) {
            this.sorts.push({ field, dir: 'asc' });
          } else {
            this.sorts = [{ field, dir: 'asc' }];
          }
        } else if (this.sorts[index].dir === 'asc') {
          this.sorts[index].dir = 'desc';
        } else if (event?.shiftKey) {
          this.sorts.splice(index, 1);
        } else {
          this.sorts = [{ field, dir: 'asc' }];
        }

        this.page = 1;
        await this.refresh();
      },

      sortDir(field) {
        const found = this.sorts.find((s) => s.field === field);
        return found ? found.dir : '';
      },

      ariaSort(field) {
        const index = this.sorts.findIndex((s) => s.field === field);
        if (index === -1) return 'none';
        if (index > 0) return 'other';
        return this.sorts[0].dir === 'desc' ? 'descending' : 'ascending';
      },

      // ---- Keyboard navigation: roving tabindex over the body cells ------------------------
      // The whole <tbody> is a single Tab stop; arrows move between cells, Enter activates the
      // cell's control, Space toggles the row selection, PageUp/PageDown change page.
      // Columns are counted visually (colspan-aware, hidden breakpoint cells skipped), so moving
      // up/down through a group header keeps the column the user was on.

      bodyRows() {
        return Array.from(document.getElementById(`${this.id}-body`)?.rows ?? []).filter((row) => !row.hasAttribute('data-spacer'));
      },

      visibleCells(row) {
        return Array.from(row?.cells ?? []).filter((cell) => cell.getClientRects().length > 0);
      },

      cellColumn(cell) {
        let column = 0;
        for (const candidate of this.visibleCells(cell.parentElement)) {
          if (candidate === cell) return column;
          column += candidate.colSpan;
        }
        return column;
      },

      cellAt(row, column) {
        const cells = this.visibleCells(row);
        let start = 0;
        for (const cell of cells) {
          if (column < start + cell.colSpan) return cell;
          start += cell.colSpan;
        }
        return cells[cells.length - 1] ?? null;
      },

      syncRoving(refocus) {
        const rows = this.bodyRows();
        if (rows.length === 0) return;

        rows.forEach((row) => {
          Array.from(row.cells).forEach((cell) => { cell.tabIndex = -1; });
          row.querySelectorAll('a[href], button, input, select, textarea').forEach((control) => { control.tabIndex = -1; });
        });

        this.cursorRow = Math.min(this.cursorRow, rows.length - 1);
        const target = this.cellAt(rows[this.cursorRow], this.cursorCol) ?? rows[0].cells[0];
        if (!target) return;

        target.tabIndex = 0;
        if (refocus) target.focus();
      },

      focusCell(cell, keepColumn) {
        if (!cell) return;
        const rows = this.bodyRows();
        rows.forEach((row) => Array.from(row.cells).forEach((c) => { c.tabIndex = -1; }));

        cell.tabIndex = 0;
        this.cursorRow = rows.indexOf(cell.parentElement);
        this.cursorAbs = cell.parentElement.dataset.row ? Number(cell.parentElement.dataset.row) : null;
        if (!keepColumn) this.cursorCol = this.cellColumn(cell);
        cell.focus();
      },

      onBodyFocusIn(event) {
        const cell = event.target.closest?.('td');
        if (!cell || cell.tabIndex === 0) return;
        const rows = this.bodyRows();
        if (!rows.includes(cell.parentElement)) return;

        rows.forEach((row) => Array.from(row.cells).forEach((c) => { c.tabIndex = -1; }));
        cell.tabIndex = 0;
        this.cursorRow = rows.indexOf(cell.parentElement);
        this.cursorAbs = cell.parentElement.dataset.row ? Number(cell.parentElement.dataset.row) : null;
        this.cursorCol = this.cellColumn(cell);
      },

      async onBodyKeydown(event) {
        const cell = event.target.closest?.('td');
        if (!cell || event.altKey || event.metaKey) return;

        const rows = this.bodyRows();
        const row = cell.parentElement;
        const r = rows.indexOf(row);
        if (r === -1) return;

        // Focus sits on a control inside the cell (entered with Enter, or clicked): Enter/Space are
        // the control's own, Left/Right walk the cell's controls, Escape goes back to the cell.
        if (event.target !== cell) {
          const controls = Array.from(cell.querySelectorAll('a[href], button, input, select'));
          const k = controls.indexOf(event.target);
          if (event.key === 'Escape') {
            event.preventDefault();
            cell.focus();
            return;
          }
          if ((event.key === 'ArrowRight' || event.key === 'ArrowLeft') && controls.length > 1) {
            const next = controls[k + (event.key === 'ArrowRight' ? 1 : -1)];
            if (next) {
              event.preventDefault();
              next.focus();
              return;
            }
          }
          if (event.key === 'Enter' || event.key === ' ') return;
        }

        const cells = this.visibleCells(row);
        const i = cells.indexOf(cell);

        switch (event.key) {
          case 'ArrowRight':
            this.focusCell(cells[i + 1]);
            break;
          case 'ArrowLeft':
            this.focusCell(cells[i - 1]);
            break;
          case 'ArrowDown':
            if (this.isVirtual() && row.dataset.row && Number(row.dataset.row) + 1 < this.meta.total) {
              this.focusVirtualRow(Number(row.dataset.row) + 1);
            } else {
              this.focusCell(this.cellAt(rows[r + 1], this.cursorCol), true);
            }
            break;
          case 'ArrowUp':
            if (this.isVirtual() && row.dataset.row && Number(row.dataset.row) > 0) {
              this.focusVirtualRow(Number(row.dataset.row) - 1);
            } else if (r === 0) {
              this.focusHeader(this.cursorCol);
            } else {
              this.focusCell(this.cellAt(rows[r - 1], this.cursorCol), true);
            }
            break;
          case 'Home':
            if (event.ctrlKey && this.isVirtual()) {
              this.cursorCol = 0;
              this.focusVirtualRow(0);
              break;
            }
            this.focusCell(event.ctrlKey ? this.visibleCells(rows[0])[0] : cells[0]);
            break;
          case 'End': {
            if (event.ctrlKey && this.isVirtual()) {
              this.cursorCol = Number.MAX_SAFE_INTEGER;
              this.focusVirtualRow(this.meta.total - 1);
              break;
            }
            const lastRow = event.ctrlKey ? this.visibleCells(rows[rows.length - 1]) : cells;
            this.focusCell(lastRow[lastRow.length - 1]);
            break;
          }
          case 'PageDown':
          case 'PageUp': {
            const delta = event.key === 'PageDown' ? 1 : -1;
            event.preventDefault();
            if (this.isVirtual()) {
              const vs = virtualStore.get(this.id);
              const card = this.root().querySelector('[data-netgrid-card]');
              const step = Math.max(1, Math.floor((card?.clientHeight ?? 400) / (vs?.rowHeight || 40)) - 1);
              const from = row.dataset.row ? Number(row.dataset.row) : 0;
              this.focusVirtualRow(Math.min(Math.max(from + delta * step, 0), this.meta.total - 1));
              return;
            }
            const next = this.page + delta;
            if (next < 1 || next > this.totalPages) return;
            this.cursorRow = 0;
            await this.go(delta);
            return;
          }
          case 'Enter': {
            const controls = cell.querySelectorAll('a[href], button, input, select');
            if (controls.length > 1) {
              controls[0].focus();                 // several actions: step into the cell
              break;
            }
            // A group row toggles from any of its cells (the subtotal cells have no control);
            // a linked row opens from any cell without a control of its own.
            const control = controls[0] ?? row.querySelector('[aria-expanded]');
            if (control) control.click();
            else if (row.dataset.href) this.openRowLink(row, event.ctrlKey || event.metaKey);
            else return;
            break;
          }
          case ' ': {
            const control = row.querySelector('input[type="checkbox"]') ?? row.querySelector('[aria-expanded]') ?? cell.querySelector('button');
            if (!control) return;
            control.click();
            break;
          }
          default:
            return;
        }

        event.preventDefault();
      },

      // ---- Row link and row actions ----------------------------------------------------------

      onRowClick(event) {
        const row = event.target.closest?.('tr[data-href]');
        if (!row || event.defaultPrevented) return;
        if (event.target.closest('a, button, input, select, textarea, label, [data-actions]')) return;
        if (window.getSelection()?.toString()) return;              // selecting text is not a click

        const newTab = event.button === 1 || event.ctrlKey || event.metaKey;
        if (event.button !== 0 && event.button !== 1) return;
        if (event.type === 'auxclick' && event.button !== 1) return;
        this.openRowLink(row, newTab);
      },

      openRowLink(row, newTab) {
        const href = row.dataset.href;
        if (!href) return;
        if (newTab) {
          window.open(href, '_blank', 'noopener');
          return;
        }

        const target = row.dataset.target;
        if (target && target !== '_self') window.open(href, target, target === '_blank' ? 'noopener' : undefined);
        else window.location.assign(href);
      },

      // Raises netgrid:action for the host page. Inside an iframe the same payload goes to the
      // parent window too, but only to a parent of the same origin.
      rowAction(action, key) {
        const detail = { grid: this.id, action, key };
        this.root().dispatchEvent(new CustomEvent('netgrid:action', { bubbles: true, detail }));
        if (window.parent !== window) {
          try {
            window.parent.postMessage({ type: 'netgrid:action', ...detail }, window.location.origin);
          } catch {
            /* cross-origin parent: nothing is sent */
          }
        }
      },

      // ---- Virtual scroll ------------------------------------------------------------------------
      // Rows arrive in blocks (page = block + 1, pageSize = blockSize). The DOM keeps the blocks
      // around the viewport; everything else is a spacer row whose height is the measured height of
      // the blocks it stands for (or an estimate from the average row height until measured).

      isVirtual() {
        return Boolean(this.virtualCfg) && this.groupBy.length === 0;
      },

      virtualReset(body) {
        const previous = virtualStore.get(this.id);
        previous?.controller.abort();

        const rows = Array.from(body.querySelectorAll('tr[data-row]'));
        const rowHeight = rows.length
          ? rows.reduce((sum, row) => sum + row.offsetHeight, 0) / rows.length
          : 40;

        const vs = {
          blocks: new Map([[0, body.innerHTML]]),
          heights: new Map(),
          pending: new Set(),
          controller: new AbortController(),
          rowHeight,
          first: 0,
          last: 0,
          framed: false
        };
        virtualStore.set(this.id, vs);

        const card = this.root().querySelector('[data-netgrid-card]');
        if (card) card.scrollTop = 0;
        this.renderVirtual();
      },

      virtualBlockCount() {
        return Math.max(1, Math.ceil(this.meta.total / this.virtualCfg.blockSize));
      },

      virtualBlockHeight(vs, block) {
        if (vs.heights.has(block)) return vs.heights.get(block);
        const size = this.virtualCfg.blockSize;
        const rowsInBlock = Math.max(0, Math.min(size, this.meta.total - block * size));
        return rowsInBlock * vs.rowHeight;
      },

      onVirtualScroll() {
        if (!this.isVirtual()) return;
        const vs = virtualStore.get(this.id);
        if (!vs || vs.framed) return;
        vs.framed = true;
        nextFrame(() => {
          vs.framed = false;
          this.updateVirtualWindow();
        });
      },

      updateVirtualWindow() {
        const vs = virtualStore.get(this.id);
        const card = this.root().querySelector('[data-netgrid-card]');
        if (!vs || !card) return;

        const head = this.root().querySelector('thead')?.offsetHeight ?? 0;
        const top = Math.max(0, card.scrollTop - head);
        const bottom = top + card.clientHeight;

        // Walk the blocks by their (measured or estimated) heights to find the visible ones.
        const count = this.virtualBlockCount();
        let offset = 0;
        let firstVisible = 0;
        let lastVisible = count - 1;
        for (let block = 0; block < count; block++) {
          const height = this.virtualBlockHeight(vs, block);
          if (offset + height <= top) firstVisible = block + 1;
          if (offset < bottom) lastVisible = block;
          offset += height;
        }

        const from = Math.max(0, Math.min(firstVisible, count - 1) - 1);
        const to = Math.min(count - 1, lastVisible + 1);

        const missing = [];
        for (let block = from; block <= to; block++) {
          if (!vs.blocks.has(block)) missing.push(block);
        }

        missing.forEach((block) => this.loadVirtualBlock(block));
        if (missing.length === 0 && (from !== vs.first || to !== vs.last)) {
          vs.first = from;
          vs.last = to;
          this.renderVirtual();
        }
      },

      async loadVirtualBlock(block) {
        const vs = virtualStore.get(this.id);
        if (!vs || vs.pending.has(block) || vs.blocks.has(block)) return;
        vs.pending.add(block);

        const params = toParams(this, null);
        params.set('page', String(block + 1));
        params.set('pageSize', String(this.virtualCfg.blockSize));

        try {
          const response = await fetch(`${PREFIX()}/${this.id}/rows?${params.toString()}`, {
            headers: { 'HX-Request': 'true' },
            signal: vs.controller.signal
          });
          if (!response.ok) throw new Error(`HTTP ${response.status}`);
          const html = await response.text();
          if (virtualStore.get(this.id) !== vs) return;   // the query changed meanwhile
          vs.blocks.set(block, html);
        } catch (error) {
          if (error?.name !== 'AbortError') console.error('[netgrid] block fetch failed:', error);
        } finally {
          vs.pending.delete(block);
        }

        this.updateVirtualWindow();
      },

      renderVirtual() {
        const vs = virtualStore.get(this.id);
        const body = document.getElementById(`${this.id}-body`);
        if (!vs || !body) return;

        // Blocks far from the viewport are dropped from memory too (they are cheap to refetch).
        for (const block of vs.blocks.keys()) {
          if (block < vs.first - 3 || block > vs.last + 3) vs.blocks.delete(block);
        }

        const focusedRow = body.contains(document.activeElement)
          ? document.activeElement.closest('tr')?.dataset.row
          : undefined;

        const span = this.headerCells().length || 1;
        const spacer = (height) => height > 0
          ? `<tr data-spacer aria-hidden="true"><td colspan="${span}" style="height:${Math.round(height)}px;padding:0;border:0"></td></tr>`
          : '';

        let above = 0;
        for (let block = 0; block < vs.first; block++) above += this.virtualBlockHeight(vs, block);
        let below = 0;
        for (let block = vs.last + 1; block < this.virtualBlockCount(); block++) below += this.virtualBlockHeight(vs, block);

        let html = spacer(above);
        for (let block = vs.first; block <= vs.last; block++) html += vs.blocks.get(block) ?? '';
        html += spacer(below);

        htmx.swap(body, html, { swapStyle: 'innerHTML' });
        this.measureVirtualBlocks(vs, body);

        this.applyPinStateAll();
        if (Object.keys(this.columnWidths).length) this.applyColumnWidths();
        this.applyPinnedOffsets();

        // Keyboard focus: a row the keyboard asked for (focusVirtualRow) is focused and scrolled
        // into view; a row that simply stays rendered keeps focus without chasing the scroll.
        const fromKeyboard = this.pendingFocusRow !== null;
        const target = fromKeyboard ? this.pendingFocusRow : (focusedRow !== undefined ? Number(focusedRow) : null);
        const row = target !== null ? body.querySelector(`tr[data-row="${target}"]`) : null;
        this.syncRoving(false);
        if (row) {
          this.pendingFocusRow = null;
          const cell = this.cellAt(row, this.cursorCol);
          if (cell) {
            this.bodyRows().forEach((r) => Array.from(r.cells).forEach((c) => { c.tabIndex = -1; }));
            cell.tabIndex = 0;
            this.cursorAbs = target;
            cell.focus({ preventScroll: !fromKeyboard });
          }
        }
      },

      // Once a block is in the DOM, its real height replaces the estimate (totals rows included).
      measureVirtualBlocks(vs, body) {
        const size = this.virtualCfg.blockSize;
        const heights = new Map();
        let block = vs.first;
        for (const row of body.rows) {
          if (row.hasAttribute('data-spacer')) continue;
          if (row.dataset.row) block = Math.floor(Number(row.dataset.row) / size);
          heights.set(block, (heights.get(block) ?? 0) + row.offsetHeight);
        }
        for (const [key, value] of heights) vs.heights.set(key, value);
      },

      // Keyboard: move to an absolute row, scrolling (and loading) it into view when needed.
      focusVirtualRow(index) {
        const body = document.getElementById(`${this.id}-body`);
        const row = body?.querySelector(`tr[data-row="${index}"]`);
        if (row) {
          this.focusCell(this.cellAt(row, this.cursorCol), true);
          return;
        }

        const vs = virtualStore.get(this.id);
        const card = this.root().querySelector('[data-netgrid-card]');
        if (!vs || !card) return;

        const size = this.virtualCfg.blockSize;
        const block = Math.floor(index / size);
        let offset = this.root().querySelector('thead')?.offsetHeight ?? 0;
        for (let b = 0; b < block; b++) offset += this.virtualBlockHeight(vs, b);
        offset += (index - block * size) * vs.rowHeight;

        this.pendingFocusRow = index;
        card.scrollTop = Math.max(0, offset - card.clientHeight / 2);
        this.updateVirtualWindow();
      },

      focusHeader(column) {
        const headerRow = this.root().querySelector('thead tr');
        const th = this.cellAt(headerRow, column);
        (th?.querySelector('button, input') ?? th)?.focus();
      },

      onHeadKeydown(event) {
        if (event.target.closest('[data-popover]')) return;
        const th = event.target.closest('th');

        if (event.altKey && (event.key === 'ArrowLeft' || event.key === 'ArrowRight') && th?.dataset.field) {
          event.preventDefault();
          const field = th.dataset.field;
          if (!this.root().querySelector('table')?.classList.contains('is-resized')) this.applyColumnWidths();
          this.setColumnWidth(field, (this.columnWidths[field] ?? th.offsetWidth) + (event.key === 'ArrowRight' ? 16 : -16));
          return;
        }

        if (event.key !== 'ArrowDown' || event.altKey) return;
        const rows = this.bodyRows();
        if (!th || rows.length === 0) return;

        event.preventDefault();
        this.cursorCol = this.cellColumn(th);
        this.focusCell(this.cellAt(rows[0], this.cursorCol), true);
      },

      hasFilter(field) {
        return Boolean(this.filters[field]);
      },

      isEmptyOp(op) {
        return op === 'is-empty' || op === 'is-not-empty';
      },

      openFilter(field, event) {
        if (this.editing === field) {
          this.cancelEditing();
          return;
        }

        this.editing = field;
        const existing = this.filters[field];
        this.editingOp = existing?.op || 'equals';
        this.editingValue = existing?.value || '';
        this.popoverFlip = {};
        this.listSearch = '';

        const anchor = event?.currentTarget;

        if (this.columnInfo(field)?.mode === 'list') {
          this.listItems[field] = null;
          this.loadListValues(field);
        }

        if (anchor) {
          requestAnimationFrame(() => this.positionPopover(field, anchor));
        }
      },

      async loadListValues(field) {
        const params = toParams(this, field);
        const query = params.toString();
        try {
          const response = await fetch(`${PREFIX()}/${this.id}/values?field=${encodeURIComponent(field)}${query ? '&' + query : ''}`);
          if (!response.ok) throw new Error(`HTTP ${response.status}`);
          const data = await response.json();

          if (this.editing !== field) return;

          const selected = new Set(
            this.filters[field]?.op === 'in' ? parseInValues(this.filters[field].value) : []
          );

          this.listItems[field] = (data.values || []).map((entry) => ({
            value: entry.value,
            count: entry.count,
            checked: selected.has(entry.value)
          }));
          this.listMeta[field] = { totalDistinct: data.totalDistinct || 0 };
        } catch (error) {
          console.error('[netgrid] value list failed:', error);
          this.listItems[field] = [];
        }
      },

      visibleListItems(field) {
        const items = this.listItems[field] || [];
        const needle = this.listSearch.trim().toLowerCase();
        return needle
          ? items.filter((item) => item.value.toLowerCase().includes(needle))
          : items;
      },

      listAllSelected(field) {
        const visible = this.visibleListItems(field);
        return visible.length > 0 && visible.every((item) => item.checked);
      },

      toggleListAll(field, checked) {
        for (const item of this.visibleListItems(field)) {
          item.checked = checked;
        }
      },

      listTruncated(field) {
        const meta = this.listMeta[field];
        return Boolean(meta && (this.listItems[field] || []).length < meta.totalDistinct);
      },

      listTruncatedLabel(field) {
        const meta = this.listMeta[field];
        if (!meta) return '';
        return L('filter.values.truncated', { shown: this.listItems[field]?.length ?? 0, total: meta.totalDistinct });
      },

      popoverClass(field) {
        const meta = (window.__NETGRID__?.columns?.[id] || []).find((c) => c.field === field);
        const flip = this.popoverFlip[field] || {};
        const x = flip.x || meta?.flipX || 'right';
        const y = flip.y || 'down';
        return `${x === 'left' ? 'left-0' : 'right-0'} ${y === 'down' ? 'top-full mt-2' : 'bottom-full mb-2'}`;
      },

      positionPopover(field, anchor) {
        if (this.editing !== field) return;
        const popover = this.root().querySelector(`[data-popover="${field}"]`);
        const card = popover?.closest('[data-netgrid-card]');
        if (!popover || !card) return;

        const p = popover.getBoundingClientRect();
        const c = card.getBoundingClientRect();
        const flip = {};

        if (p.left < c.left + 1) flip.x = 'right';
        else if (p.right > c.right - 1) flip.x = 'left';

        if (p.bottom > c.bottom + 1) flip.y = 'up';

        if (flip.x || flip.y) {
          this.popoverFlip = { [field]: flip };
        }
      },

      cancelEditing() {
        this.popoverFlip = {};
        this.editing = null;
      },

      async applyFilter() {
        if (this.editing === null) return;
        const field = this.editing;

        if (this.columnInfo(field)?.mode === 'list') {
          const selected = (this.listItems[field] || [])
            .filter((item) => item.checked)
            .map((item) => item.value);

          if (selected.length > 0) {
            this.filters[field] = { op: 'in', value: JSON.stringify(selected) };
          } else {
            delete this.filters[field];
          }

          this.editing = null;
          this.page = 1;
          await this.refresh();
          return;
        }

        const value = this.editingValue.trim();

        if (isEmptyOp(this.editingOp) || value !== '') {
          this.filters[field] = { op: this.editingOp, value };
        } else {
          delete this.filters[field];
        }

        this.editing = null;
        this.page = 1;
        await this.refresh();
      },

      async clearFilter(field) {
        if (field === null || field === undefined) return;
        delete this.filters[field];
        this.page = 1;
        await this.refresh();
      },

      onSearch() {
        this.page = 1;
        return this.refresh();
      },

      async go(delta) {
        const next = this.page + delta;
        if (next < 1 || next > this.totalPages) return;
        this.page = next;
        await this.refresh();
      },

      onPageSize() {
        this.page = 1;
        return this.refresh();
      },

      currentPageIds() {
        const body = document.getElementById(`${id}-body`);
        if (!body) return [];
        return Array.from(body.querySelectorAll('tr[data-id]'), (row) => row.dataset.id);
      },

      isSelected(key) {
        return this.selected.includes(key);
      },

      toggleSelection(key) {
        const index = this.selected.indexOf(key);
        if (index === -1) {
          this.selected.push(key);
        } else {
          this.selected.splice(index, 1);
        }
      },

      togglePageAll(checked) {
        const ids = this.currentPageIds();
        if (checked) {
          this.selected = Array.from(new Set([...this.selected, ...ids]));
        } else {
          this.selected = this.selected.filter((key) => !ids.includes(key));
        }
      },

      pruneSelection() {
        const live = new Set(this.currentPageIds());
        this.selected = this.selected.filter((key) => live.has(key));
      },

      clearSelection() {
        this.selected = [];
      },

      toggleTheme() {
        const dark = document.documentElement.classList.toggle('dark');
        try {
          localStorage.setItem(THEME_KEY, dark ? 'dark' : 'light');
        } catch {
          /* storage unavailable; theme stays session-only */
        }
      }
    }));
  });
})();
