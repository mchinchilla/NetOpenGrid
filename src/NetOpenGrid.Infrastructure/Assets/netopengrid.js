/* NetOpenGrid client runtime: Alpine.js component + HTMX wiring. ES2020, async/await only. */
(() => {
  'use strict';

  const OP_LABELS = {
    'equals': 'equals',
    'not-equals': 'not equals',
    'contains': 'contains',
    'starts-with': 'starts with',
    'ends-with': 'ends with',
    'gt': 'greater than',
    'gte': 'greater or equal',
    'lt': 'less than',
    'lte': 'less or equal',
    'is-empty': 'is empty',
    'is-not-empty': 'is not empty'
  };

  const THEME_KEY = 'netgrid:theme';
  const MAX_SEARCH = 200;

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
    if (state.columnOrder?.length) params.set('cols', state.columnOrder.join(','));
    return params;
  }

  async function fetchRows(state) {
    if (typeof htmx === 'undefined') {
      throw new Error('NetOpenGrid: htmx is not loaded.');
    }

    const params = toParams(state);
    const query = params.toString();
    window.history.replaceState(null, '', `${window.location.pathname}${query ? '?' + query : ''}`);
    state.loading = true;
    try {
      await htmx.ajax('GET', `/netgrid/${state.id}/rows${query ? '?' + query : ''}`, {
        target: `#${state.id}-body`,
        swap: 'innerHTML'
      });
    } catch (error) {
      console.error('[netgrid] row fetch failed:', error);
    } finally {
      state.loading = false;
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

      get totalPages() {
        return Math.max(1, this.meta.pages);
      },

      get rangeLabel() {
        if (this.meta.total === 0) return 'No records';
        const from = (this.page - 1) * this.pageSize + 1;
        const to = Math.min(this.page * this.pageSize, this.meta.total);
        return `${from}-${to} of ${this.meta.total}`;
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
          return values.length > 2 ? `any of ${shown} +${values.length - 2}` : `any of ${shown}`;
        }

        return `${OP_LABELS[f.op] || f.op}${isEmptyOp(f.op) ? '' : ` ${f.value}`}`;
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
        this.seedFromUrl();
        this.loadPinnedState();
        this.applyPinStateAll();

        document.body.addEventListener('htmx:afterRequest', (event) => {
          const config = event.detail?.requestConfig;
          if (!config?.path || !config.path.includes(`/netgrid/${id}/rows`)) return;
          const xhr = event.detail.xhr;
          if (!xhr) return;

          const total = xhr.getResponseHeader('X-Grid-Total');
          if (total !== null) {
            this.meta = {
              total: parseInt(total, 10) || 0,
              page: parseInt(xhr.getResponseHeader('X-Grid-Page'), 10) || this.page,
              pageSize: parseInt(xhr.getResponseHeader('X-Grid-Page-Size'), 10) || this.pageSize,
              pages: Math.max(1, parseInt(xhr.getResponseHeader('X-Grid-Page-Count'), 10) || 1)
            };
            this.page = this.meta.page;
            this.pageSize = this.meta.pageSize;
          }

          this.applyPinStateAll();
          requestAnimationFrame(() => this.applyPinnedOffsets());
        });

        window.addEventListener('resize', () => this.applyPinnedOffsets());
        requestAnimationFrame(() => this.applyPinnedOffsets());

        if (this.columnOrder?.length) {
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

      loadPinnedState() {
        const serverPins = (window.__NETGRID__?.columns?.[id] || [])
          .filter((c) => c.pin)
          .map((c) => c.field);

        let saved = [];
        try {
          const raw = localStorage.getItem(`netgrid:pins:${id}`);
          if (raw) saved = JSON.parse(raw);
        } catch {
          saved = [];
        }

        this.pinnedFields = [...new Set([...serverPins, ...saved])];
      },

      isPinned(field) {
        return this.pinnedFields.includes(field);
      },

      togglePin(field) {
        const next = this.isPinned(field)
          ? this.pinnedFields.filter((f) => f !== field)
          : [...this.pinnedFields, field];

        this.pinnedFields = next;

        try {
          localStorage.setItem(`netgrid:pins:${id}`, JSON.stringify(next));
        } catch {
          /* storage unavailable; pin stays session-only */
        }

        this.applyPinStateAll();
        this.applyPinnedOffsets();
      },

      applyPinStateAll() {
        for (const field of this.pinnedFields) {
          this.applyPinState(field, true);
        }
      },

      applyPinState(field, pinned) {
        const targets = this.$el.querySelectorAll(`th[data-field="${field}"], td[data-field="${field}"]`);
        targets.forEach((el) => {
          const classes = el.tagName === 'TH'
            ? ['sticky', 'z-30', 'border-r', 'border-neutral-200', 'bg-neutral-50', 'dark:border-neutral-800', 'dark:bg-neutral-800/60']
            : ['sticky', 'z-10', 'border-r', 'border-neutral-100', 'bg-white', 'hover:bg-brand-50/40', 'dark:border-neutral-800/60', 'dark:bg-neutral-900', 'dark:hover:bg-white/[0.04]'];

          if (pinned) {
            el.classList.add(...classes);
            el.setAttribute('data-pin', field);
          } else {
            el.classList.remove(...classes);
            el.removeAttribute('data-pin');
          }
        });
      },

      applyPinnedOffsets() {
        this.$el.querySelectorAll('[data-pin]').forEach((el) => {
          el.style.left = `${el.offsetLeft}px`;
        });
      },

      onColumnDragStart(field, event) {
        this.dragField = field;
        event.dataTransfer?.setData('text/plain', field);
        event.dataTransfer && (event.dataTransfer.effectAllowed = 'move');
      },

      async onColumnDrop(field, event) {
        const dragged = this.dragField || event.dataTransfer?.getData('text/plain');
        this.dragField = null;
        if (!dragged || dragged === field) return;

        const order = Array.from(
          this.$el.querySelectorAll('th[data-field]'),
          (th) => th.dataset.field
        );
        const from = order.indexOf(dragged);
        const to = order.indexOf(field);
        if (from === -1 || to === -1) return;

        order.splice(to, 0, order.splice(from, 1)[0]);
        this.columnOrder = order;

        try {
          localStorage.setItem(`netgrid:cols:${id}`, JSON.stringify(order));
        } catch {
          /* storage unavailable; order stays session-only */
        }

        await this.refresh();
      },
      },

      seedFromUrl() {
        const params = new URLSearchParams(window.location.search);

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
      },

      exportCsv() {
        const params = toParams(this, null);
        const query = params.toString();
        window.location.href = `/netgrid/${this.id}/export${query ? '?' + query : ''}`;
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
          const response = await fetch(`/netgrid/${this.id}/values?field=${encodeURIComponent(field)}${query ? '&' + query : ''}`);
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
        return `Showing ${this.listItems[field]?.length ?? 0} of ${meta.totalDistinct} values`;
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
        const popover = this.$el.querySelector(`[data-popover="${field}"]`);
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
