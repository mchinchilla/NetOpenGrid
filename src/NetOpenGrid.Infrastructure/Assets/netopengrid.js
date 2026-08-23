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

  function toParams(state) {
    const params = new URLSearchParams();
    if (state.page > 1) params.set('page', String(state.page));
    if (!state.pageSizeLocked) params.set('pageSize', String(state.pageSize));
    for (const sort of state.sorts) {
      params.append('sort', `${sort.field}:${sort.dir}`);
    }
    for (const [field, filter] of Object.entries(state.filters)) {
      if (!filter) continue;
      if (isEmptyOp(filter.op)) {
        params.append('filter', `${field}:${filter.op}`);
      } else {
        params.append('filter', `${field}:${filter.op}:${filter.value}`);
      }
    }
    if (state.q) params.set('q', state.q.slice(0, MAX_SEARCH));
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
            label: `${this.columnHeader(field)} ${OP_LABELS[f.op] || f.op}${isEmptyOp(f.op) ? '' : ` ${f.value}`}`
          }));
      },

      get allPageSelected() {
        const ids = this.currentPageIds();
        return ids.length > 0 && ids.every((key) => this.selected.includes(key));
      },

      get editingLabel() {
        return this.editing === null ? '' : this.columnHeader(this.editing);
      },

      columnHeader(field) {
        const columns = window.__NETGRID__?.columns?.[id] || [];
        const column = columns.find((c) => c.field === field);
        return column ? column.header : field;
      },

      init() {
        const initial = window.__NETGRID__?.initial?.[id];
        if (initial) {
          this.meta = { ...initial };
          this.page = initial.page ?? 1;
          this.pageSize = initial.pageSize ?? 25;
        }

        this.seedFromUrl();
        this.pageSizeLocked = false;

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
        });
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

        for (const raw of params.getAll('filter').flatMap((value) => value.split(','))) {
          const parts = raw.split(':');
          if (parts.length < 2) continue;
          const [field, op] = parts;
          const value = parts.length >= 3 ? parts.slice(2).join(':') : '';
          this.filters[field] = { op, value };
        }

        const q = params.get('q');
        if (q) this.q = q;
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

        const anchor = event?.currentTarget;
        if (anchor) {
          requestAnimationFrame(() => this.positionPopover(field, anchor));
        }
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
