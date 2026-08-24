using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.ObjectPool;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;
using NetOpenGrid.Infrastructure.Runtime;

namespace NetOpenGrid.Infrastructure.Rendering;

/// <summary>
/// High-performance HTML renderer: pooled builders plus HtmlEncoder writing
/// directly into the output buffer (no intermediate strings per cell).
/// Emits the HTMX + Alpine.js client contract.
/// </summary>
public sealed class GridHtmlRenderer<TItem>
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string SearchIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\" fill=\"currentColor\" class=\"h-4 w-4\"><path fill-rule=\"evenodd\" d=\"M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z\" clip-rule=\"evenodd\" /></svg>";

    private const string SunIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"currentColor\" class=\"h-5 w-5 dark:hidden\"><path d=\"M12 2.25a.75.75 0 0 1 .75.75v2.25a.75.75 0 0 1-1.5 0V3a.75.75 0 0 1 .75-.75ZM7.5 12a4.5 4.5 0 1 1 9 0 4.5 4.5 0 0 1-9 0ZM18.894 6.166a.75.75 0 0 0-1.06-1.06l-1.591 1.59a.75.75 0 1 0 1.06 1.061l1.591-1.59ZM21.75 12a.75.75 0 0 1-.75.75h-2.25a.75.75 0 0 1 0-1.5H21a.75.75 0 0 1 .75.75ZM17.834 18.894a.75.75 0 0 0 1.06-1.06l-1.59-1.591a.75.75 0 1 0-1.061 1.06l1.59 1.591ZM12 18a.75.75 0 0 1 .75.75V21a.75.75 0 0 1-1.5 0v-2.25A.75.75 0 0 1 12 18ZM7.758 17.303a.75.75 0 0 0-1.061-1.06l-1.591 1.59a.75.75 0 0 0 1.06 1.061l1.59-1.591ZM6 12a.75.75 0 0 1-.75.75H3a.75.75 0 0 1 0-1.5h2.25A.75.75 0 0 1 6 12ZM6.697 7.757a.75.75 0 0 0 1.06-1.06l-1.59-1.591a.75.75 0 0 0-1.061 1.06l1.59 1.591Z\" /></svg>";

    private const string MoonIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"currentColor\" class=\"hidden h-5 w-5 dark:block\"><path fill-rule=\"evenodd\" d=\"M9.528 1.718a.75.75 0 0 1 .162.819A8.97 8.97 0 0 0 9 6a9 9 0 0 0 9 9 8.97 8.97 0 0 0 3.463-.69.75.75 0 0 1 .981.98 10.503 10.503 0 0 1-9.694 6.46c-5.799 0-10.5-4.7-10.5-10.5 0-4.368 2.667-8.1 6.46-9.694a.75.75 0 0 1 .818.162Z\" clip-rule=\"evenodd\" /></svg>";

    private const string FunnelIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\" fill=\"currentColor\" class=\"h-3.5 w-3.5\"><path d=\"M2.628 1.601C5.028 1.206 7.49 1 10 1s4.973.206 7.372.601a.75.75 0 0 1 .628.74v2.288a2.25 2.25 0 0 1-.659 1.59l-4.682 4.683a2.25 2.25 0 0 0-.659 1.59v3.037c0 .684-.31 1.33-.844 1.757l-1.937 1.55A.75.75 0 0 1 8 18.25v-5.757a2.25 2.25 0 0 0-.659-1.591L2.659 6.22A2.25 2.25 0 0 1 2 4.629V2.34a.75.75 0 0 1 .628-.74Z\" /></svg>";

    private const string PinIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"currentColor\" class=\"h-3 w-3\"><path fill-rule=\"evenodd\" d=\"M15.5 2.5a1 1 0 0 1 .7.3l5 5a1 1 0 0 1-.3 1.63l-3.1 1.38-2.6 2.6-.9 4.44a1 1 0 0 1-1.68.5l-3.4-3.4-4.4 4.4a1 1 0 0 1-1.4-1.42l4.4-4.4-3.4-3.4a1 1 0 0 1 .5-1.68l4.44-.9 2.6-2.6 1.38-3.1a1 1 0 0 1 .86-.55Z\" clip-rule=\"evenodd\" /></svg>";

    private const string DownloadIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"currentColor\" class=\"h-4 w-4\"><path d=\"M12 2.25a.75.75 0 0 1 .75.75v11.69l3.22-3.22a.75.75 0 1 1 1.06 1.06l-4.5 4.5a.75.75 0 0 1-1.06 0l-4.5-4.5a.75.75 0 1 1 1.06-1.06l3.22 3.22V3a.75.75 0 0 1 .75-.75Z\" /><path d=\"M3.75 15a.75.75 0 0 1 .75.75v2.25A1.5 1.5 0 0 0 6 19.5h12a1.5 1.5 0 0 0 1.5-1.5v-2.25a.75.75 0 0 1 1.5 0V18a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3v-2.25A.75.75 0 0 1 3.75 15Z\" /></svg>";

    private const string ChevronIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\" fill=\"currentColor\" class=\"h-3.5 w-3.5\"><path fill-rule=\"evenodd\" d=\"M7.21 14.77a.75.75 0 0 1 .02-1.06L11.168 10 7.23 6.29a.75.75 0 1 1 1.04-1.08l4.5 4.25a.75.75 0 0 1 0 1.08l-4.5 4.25a.75.75 0 0 1-1.06-.02Z\" clip-rule=\"evenodd\" /></svg>";

    private const string SpinnerIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" fill=\"none\" viewBox=\"0 0 24 24\" class=\"h-5 w-5 animate-spin text-brand-600 dark:text-brand-400\"><circle class=\"opacity-25\" cx=\"12\" cy=\"12\" r=\"10\" stroke=\"currentColor\" stroke-width=\"4\"></circle><path class=\"opacity-75\" fill=\"currentColor\" d=\"M4 12a8 8 0 0 1 8-8v4a4 4 0 0 0-4 4H4z\"></path></svg>";

    private const string ThemeBootScript =
        "<script>(function(){try{var t=localStorage.getItem('netgrid:theme');var d=t?t==='dark':window.matchMedia('(prefers-color-scheme: dark)').matches;if(d){document.documentElement.classList.add('dark');}}catch(e){}})();</script>";

    private readonly GridOptions<TItem> _options;
    private readonly NetOpenGridAssetOptions _assetOptions;
    private readonly NetOpenGridLocalizationOptions _locale;
    private readonly IReadOnlyList<GridColumn<TItem>> _visibleColumns;
    private readonly ObjectPool<StringBuilder> _stringBuilderPool;

    public GridHtmlRenderer(GridOptions<TItem> options, NetOpenGridAssetOptions assetOptions, NetOpenGridLocalizationOptions? localizationOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(assetOptions);
        _options = options;
        _assetOptions = assetOptions;
        _locale = localizationOptions ?? new NetOpenGridLocalizationOptions();
        _visibleColumns = options.Columns.Where(static c => c.IsVisible).ToArray();
        _stringBuilderPool = new DefaultObjectPoolProvider()
            .CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 256 * 1024);
    }

    private string L(string key) => _locale[key];

    public ValueTask<string> RenderRowsAsync(GridExecutionResult<TItem> result, IReadOnlyList<GridColumn<TItem>>? columnOrder = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var columns = ResolveColumns(columnOrder);
        var sb = _stringBuilderPool.Get();
        try
        {
            using var writer = new StringWriter(sb);
            AppendRows(writer, result.Page, columns);
            return ValueTask.FromResult(sb.ToString());
        }
        finally
        {
            _stringBuilderPool.Return(sb.Clear());
        }
    }

    /// <summary>Grouped tbody: header rows (collapsible, nested) + expanded leaf rows.</summary>
    public ValueTask<string> RenderGroupedRowsAsync(
        GroupedPageResult<TItem> grouped,
        IReadOnlyList<GridColumn<TItem>>? columnOrder = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var columns = ResolveColumns(columnOrder);
        var sb = _stringBuilderPool.Get();
        try
        {
            using var writer = new StringWriter(sb);

            if (grouped.Groups.Count == 0)
            {
                AppendEmptyRow(writer, columns);
            }
            else
            {
                foreach (var group in grouped.Groups)
                {
                    AppendGroup(writer, group, columns);
                }
            }

            return ValueTask.FromResult(sb.ToString());
        }
        finally
        {
            _stringBuilderPool.Return(sb.Clear());
        }
    }

    private void AppendGroup(TextWriter w, GridGroup<TItem> group, IReadOnlyList<GridColumn<TItem>> columns)
    {
        var expanded = group.Children.Count > 0 || group.Rows.Count > 0;
        var label = group.Value.Length == 0 ? L("group.blank") : group.Value;
        var indent = 8 + group.Level * 20;

        w.Write("<tr class=\"group-header bg-neutral-50/80 dark:bg-neutral-800/40\" data-group-path=\"");
        AppendEncoded(w, group.Path);
        w.Write("\">");
        w.Write("<td colspan=\"");
        w.Write((columns.Count + (_options.EnableRowSelection ? 1 : 0)).ToString(CultureInfo.InvariantCulture));
        w.Write("\" class=\"px-4 py-2\">");
        w.Write("<div class=\"flex items-center gap-2\" style=\"padding-left:");
        w.Write(indent.ToString(CultureInfo.InvariantCulture));
        w.Write("px\">");

        w.Write("<button type=\"button\" class=\"inline-flex cursor-pointer items-center text-neutral-500 transition hover:text-brand-600 dark:hover:text-brand-400\" @click=\"toggleGroup(");
        AppendJsQuoted(w, group.Path);
        w.Write(")\" aria-expanded=\"");
        w.Write(expanded ? "true" : "false");
        w.Write("\" aria-label=\"");
        w.Write(L("group.toggle.aria"));
        w.Write("\">");
        w.Write("<span class=\"inline-flex transition-transform ");
        if (expanded)
        {
            w.Write("rotate-90");
        }

        w.Write("\">");
        w.Write(ChevronIcon);
        w.Write("</span>");
        w.Write("</button>");

        w.Write("<span class=\"text-sm font-semibold\">");
        AppendEncoded(w, label);
        w.Write("</span>");

        w.Write("<span class=\"badge badge-muted tabular-nums\">");
        w.Write(group.Count.ToString(CultureInfo.InvariantCulture));
        w.Write("</span>");

        w.Write("</div></td></tr>");

        foreach (var child in group.Children)
        {
            AppendGroup(w, child, columns);
        }

        foreach (var row in group.Rows)
        {
            AppendDataRow(w, row, columns);
        }
    }

    private void AppendEmptyRow(TextWriter w, IReadOnlyList<GridColumn<TItem>> columns)
    {
        w.Write("<tr><td colspan=\"");
        w.Write((columns.Count + (_options.EnableRowSelection ? 1 : 0)).ToString(CultureInfo.InvariantCulture));
        w.Write("\" class=\"px-4 py-12 text-center text-sm text-neutral-500 dark:text-neutral-400\">");
        AppendEncoded(w, _options.EmptyMessage);
        w.Write("</td></tr>");
    }

    private IReadOnlyList<GridColumn<TItem>> ResolveColumns(IReadOnlyList<GridColumn<TItem>>? columnOrder)
    {
        if (columnOrder is null || columnOrder.Count == 0)
        {
            return _visibleColumns;
        }

        var merged = new List<GridColumn<TItem>>(columnOrder);
        foreach (var column in _visibleColumns)
        {
            if (!merged.Contains(column))
            {
                merged.Add(column);
            }
        }

        return merged;
    }

    public ValueTask<string> RenderShellAsync(GridExecutionResult<TItem> initialResult, IReadOnlyList<GridColumn<TItem>>? columnOrder = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sb = _stringBuilderPool.Get();
        try
        {
            using var writer = new StringWriter(sb);
            AppendDocumentStart(writer);
            AppendSiteHeader(writer);
            writer.Write("<main class=\"mx-auto max-w-7xl px-4 py-8\">");
            AppendGridRoot(writer, initialResult, ResolveColumns(columnOrder));
            writer.Write("</main>");
            AppendStateScript(writer, initialResult.Page);
            writer.Write("</body></html>");

            return ValueTask.FromResult(sb.ToString());
        }
        finally
        {
            _stringBuilderPool.Return(sb.Clear());
        }
    }

    private void AppendDocumentStart(TextWriter w)
    {
        var prefix = _assetOptions.NormalizedAssetPrefix;

        w.Write("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        w.Write("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        w.Write("<title>");
        AppendEncoded(w, _options.Title);
        w.Write("</title>");
        w.Write(ThemeBootScript);
        w.Write("<link rel=\"stylesheet\" href=\"");
        w.Write(_assetOptions.NormalizedCssPath);
        w.Write('/');
        w.Write(_assetOptions.CssFilePrefix);
        AppendEncoded(w, _options.Theme);
        w.Write(".css\">");
        w.Write("<style>[x-cloak]{display:none!important}</style>");
        w.Write("<script src=\"");
        w.Write(prefix);
        w.Write("/netopengrid.js?v=");
        w.Write(Assets.EmbeddedGridAssets.ClientRuntime.Version);
        w.Write("\" defer></script>");
        w.Write("<script src=\"");
        w.Write(prefix);
        w.Write("/vendor/htmx.min.js?v=");
        w.Write(Assets.EmbeddedGridAssets.Htmx.Version);
        w.Write("\" defer></script>");
        w.Write("<script src=\"");
        w.Write(prefix);
        w.Write("/vendor/alpine.min.js?v=");
        w.Write(Assets.EmbeddedGridAssets.Alpine.Version);
        w.Write("\" defer></script>");
        w.Write("</head><body class=\"min-h-screen bg-neutral-100 text-neutral-900 antialiased dark:bg-neutral-950 dark:text-neutral-100\">");
    }

    private void AppendSiteHeader(TextWriter w)
    {
        if (_options.NavLinks.Count == 0)
        {
            return;
        }

        w.Write("<header class=\"sticky top-0 z-40 border-b border-neutral-200 bg-white/80 backdrop-blur dark:border-neutral-800 dark:bg-neutral-900/80\">");
        w.Write("<div class=\"mx-auto flex max-w-7xl items-center gap-6 px-4 py-3\">");
        w.Write("<span class=\"font-semibold tracking-tight\">NetOpenGrid</span><nav class=\"flex gap-1 text-sm\">");

        foreach (var link in _options.NavLinks)
        {
            w.Write("<a href=\"");
            AppendEncoded(w, link.Href);
            w.Write("\" class=\"rounded-lg px-3 py-1.5 font-medium text-neutral-600 transition hover:bg-neutral-100 hover:text-neutral-900 dark:text-neutral-300 dark:hover:bg-neutral-800 dark:hover:text-white\">");
            AppendEncoded(w, link.Label);
            w.Write("</a>");
        }

        w.Write("</nav></div></header>");
    }

    private void AppendGridRoot(TextWriter w, GridExecutionResult<TItem> initialResult, IReadOnlyList<GridColumn<TItem>> columns)
    {
        w.Write("<div id=\"");
        AppendEncoded(w, _options.Id);
        w.Write("\" x-data=\"netgrid(");
        AppendJsQuoted(w, _options.Id);
        w.Write(")\" class=\"space-y-4\">");

        AppendToolbar(w);
        AppendFilterChips(w);
        AppendTableCard(w, initialResult, columns);
        AppendPager(w);

        if (_options.EnableRowSelection && _options.RowKey is not null)
        {
            AppendSelectionBar(w);
        }

        w.Write("</div>");
    }

    private void AppendToolbar(TextWriter w)
    {
        var debounceMs = Math.Max(_options.DebounceMilliseconds, 0);

        w.Write("<div class=\"flex flex-wrap items-center gap-3\">");
        w.Write("<h1 class=\"text-lg font-semibold tracking-tight\">");
        AppendEncoded(w, _options.Title);
        w.Write("</h1>");

        if (_options.Subtitle.Length > 0)
        {
            w.Write("<p class=\"text-sm text-neutral-500 dark:text-neutral-400\">");
            AppendEncoded(w, _options.Subtitle);
            w.Write("</p>");
        }

        w.Write("<span class=\"text-sm text-neutral-500 dark:text-neutral-400\" x-text=\"recordsLabel\"></span>");

        w.Write("<div class=\"relative ml-auto w-full max-w-xs sm:w-64\">");
        w.Write("<span class=\"pointer-events-none absolute left-3 top-2 text-neutral-400\">");
        w.Write(SearchIcon);
        w.Write("</span>");
        w.Write("<input type=\"search\" x-model=\"q\" @input.debounce.");
        w.Write(debounceMs.ToString(CultureInfo.InvariantCulture));
        w.Write("ms=\"onSearch()\" placeholder=\"");
        w.Write(L("search.placeholder"));
        w.Write("\" aria-label=\"");
        w.Write(L("search.aria"));
        w.Write("\" class=\"input-base pl-9\">");
        w.Write("</div>");

        w.Write("<select aria-label=\"");
        w.Write(L("group.aria"));
        w.Write("\" @change=\"addGroupBy($event.target.value); $event.target.value = ''\" class=\"input-base !w-auto py-2 text-sm\">");
        w.Write("<option value=\"\">");
        w.Write(L("group.placeholder"));
        w.Write("</option>");

        foreach (var column in _visibleColumns)
        {
            w.Write("<option value=\"");
            AppendEncoded(w, column.Field);
            w.Write("\">");
            AppendEncoded(w, column.Header);
            w.Write("</option>");
        }

        w.Write("</select>");

        w.Write("<button type=\"button\" @click=\"toggleTheme()\" class=\"btn-icon\" aria-label=\"");
        w.Write(L("theme.aria"));
        w.Write("\">");
        w.Write(SunIcon);
        w.Write(MoonIcon);
        w.Write("</button>");

        w.Write("<button type=\"button\" @click=\"exportCsv()\" class=\"btn-icon\" aria-label=\"");
        w.Write(L("export.aria"));
        w.Write("\">");
        w.Write(DownloadIcon);
        w.Write("</button>");

        w.Write("<span x-show=\"loading\" x-cloak aria-hidden=\"true\">");
        w.Write(SpinnerIcon);
        w.Write("</span>");
        w.Write("</div>");
    }

    private void AppendFilterChips(TextWriter w)
    {
        w.Write("<div class=\"flex flex-wrap items-center gap-2\" x-show=\"groupBy.length\" x-cloak>");
        w.Write("<template x-for=\"g in groupBy\" :key=\"g\">");
        w.Write("<button type=\"button\" @click=\"removeGroupBy(g)\" class=\"chip\" title=\"");
        w.Write(L("group.aria"));
        w.Write("\">");
        w.Write("<span x-text=\"columnInfo(g)?.header || g\"></span><span aria-hidden=\"true\">&times;</span>");
        w.Write("</button></template></div>");

        w.Write("<div class=\"flex flex-wrap items-center gap-2\" x-show=\"activeFilters.length\" x-cloak>");
        w.Write("<template x-for=\"entry in activeFilters\" :key=\"entry.field\">");
        w.Write("<button type=\"button\" @click=\"clearFilter(entry.field)\" class=\"chip\" title=\"");
        w.Write(L("chips.remove"));
        w.Write("\">");
        w.Write("<span x-text=\"entry.label\"></span><span aria-hidden=\"true\">&times;</span>");
        w.Write("</button></template></div>");
    }

    private void AppendTableCard(TextWriter w, GridExecutionResult<TItem> initialResult, IReadOnlyList<GridColumn<TItem>> columns)
    {
        w.Write("<div data-netgrid-card");

        if (_options.MinHeight.Length > 0)
        {
            w.Write(" style=\"min-height:");
            AppendEncoded(w, _options.MinHeight);
            w.Write('"');
        }

        w.Write(" class=\"relative overflow-x-auto rounded-2xl border border-neutral-200 bg-white shadow-sm dark:border-neutral-800 dark:bg-neutral-900\">");
        w.Write("<table class=\"min-w-full text-sm\">");

        AppendTableHead(w, columns);

        w.Write("<tbody id=\"");
        AppendEncoded(w, _options.Id);
        w.Write("-body\">");

        AppendRows(w, initialResult.Page, columns);

        w.Write("</tbody></table>");
        w.Write("</div>");
    }

    private void AppendTableHead(TextWriter w, IReadOnlyList<GridColumn<TItem>> columns)
    {
        w.Write("<thead class=\"bg-neutral-50 dark:bg-neutral-800/60\"><tr>");

        if (_options.EnableRowSelection && _options.RowKey is not null)
        {
            w.Write("<th scope=\"col\" data-pin=\"__select\" style=\"left:0\" class=\"sticky z-30 w-10 border-r border-neutral-200 bg-neutral-50 px-4 py-3 dark:border-neutral-800 dark:bg-neutral-800/60\">");
            w.Write("<input type=\"checkbox\" class=\"h-4 w-4 accent-brand-600\" aria-label=\"");
        w.Write(L("select.all.aria"));
        w.Write("\" :checked=\"allPageSelected\" @change=\"togglePageAll($event.target.checked)\">");
            w.Write("</th>");
        }

        var firstFilterableSeen = false;

        foreach (var column in columns)
        {
            var isFirstFilterable = column.IsFilterable && !firstFilterableSeen;
            if (isFirstFilterable)
            {
                firstFilterableSeen = true;
            }

            w.Write("<th scope=\"col\" draggable=\"true\" @dragstart=\"onColumnDragStart(");
            AppendJsQuoted(w, column.Field);
            w.Write(", $event)\" @dragover.prevent @drop=\"onColumnDrop(");
            AppendJsQuoted(w, column.Field);
            w.Write(", $event)\" data-field=\"");
            AppendEncoded(w, column.Field);

            if (column.IsPinned)
            {
                w.Write("\" data-pin=\"");
                AppendEncoded(w, column.Field);
                w.Write("\" style=\"left:0\" class=\"sticky z-30 relative border-r border-neutral-200 bg-neutral-50 dark:border-neutral-800 dark:bg-neutral-800/60 ");
            }
            else
            {
                w.Write("\" class=\"relative ");
            }

            w.Write("px-4 py-3 font-medium text-neutral-600 select-none dark:text-neutral-300 ");
            w.Write(TextAlignClass(column.Align));
            if (column.WidthCss is not null)
            {
                w.Write(' ');
                w.Write(column.WidthCss);
            }

            w.Write("\"><div class=\"inline-flex items-center gap-1 ");
            w.Write(column.Align switch
            {
                ColumnAlign.Center => "justify-center",
                ColumnAlign.End => "justify-end",
                _ => "justify-start"
            });
            w.Write("\">");

            if (column.IsSortable)
            {
                w.Write("<button type=\"button\" class=\"inline-flex cursor-pointer items-center gap-1 transition hover:text-brand-600 dark:hover:text-brand-400\" @click=\"sortBy(");
                AppendJsQuoted(w, column.Field);
                w.Write(", $event)\">");
                AppendEncoded(w, column.Header);
                w.Write("<span class=\"text-[10px] leading-none opacity-70\" x-show=\"sortDir(");
                AppendJsQuoted(w, column.Field);
                w.Write(") !== ''\" x-text=\"sortDir(");
                AppendJsQuoted(w, column.Field);
                w.Write(") === 'asc' ? '&#9650;' : '&#9660;'\"></span></button>");
            }
            else
            {
                w.Write("<span>");
                AppendEncoded(w, column.Header);
                w.Write("</span>");
            }

            if (column.IsFilterable)
            {
                w.Write("<button type=\"button\" class=\"cursor-pointer transition hover:text-brand-600 dark:hover:text-brand-400\" :class=\"hasFilter(");
                AppendJsQuoted(w, column.Field);
                w.Write(") ? 'text-brand-600 dark:text-brand-400' : 'text-neutral-400'\" @click=\"openFilter(");
                AppendJsQuoted(w, column.Field);
                w.Write(", $event)\" aria-label=\"");
                w.Write(L("filter.aria").Replace("{field}", column.Header));
                w.Write("\">");
                w.Write(FunnelIcon);
                w.Write("</button>");
            }

            w.Write("<button type=\"button\" class=\"cursor-pointer transition\" :class=\"isPinned(");
            AppendJsQuoted(w, column.Field);
            w.Write(") ? 'text-brand-600 dark:text-brand-400' : 'text-neutral-300 hover:text-neutral-500 dark:text-neutral-600 dark:hover:text-neutral-400'\" @click=\"togglePin(");
            AppendJsQuoted(w, column.Field);
            w.Write(")\" aria-label=\"");
            w.Write(L("pin.aria").Replace("{field}", column.Header));
            w.Write("\">");
            w.Write(PinIcon);
            w.Write("</button>");

            w.Write("</div>");

            if (column.IsFilterable)
            {
                AppendFilterPopover(w, column, isFirstFilterable);
            }

            w.Write("</th>");
        }

        w.Write("</tr></thead>");
    }

    /// <summary>Excel-like filter popover anchored to its own column header, opening inward.</summary>
    /// <summary>Excel-style filter popover anchored to its own column header, opening inward.</summary>
    private void AppendFilterPopover(TextWriter w, GridColumn<TItem> column, bool isFirstFilterable)
    {
        w.Write("<div x-show=\"editing === ");
        AppendJsQuoted(w, column.Field);
        w.Write("\" x-cloak @click.outside=\"cancelEditing()\" data-popover=\"");
        AppendEncoded(w, column.Field);
        w.Write("\" :class=\"popoverClass(");
        AppendJsQuoted(w, column.Field);
        w.Write(")\" class=\"absolute z-30 w-64 space-y-3 rounded-xl border border-neutral-200 bg-white p-3 text-left shadow-xl dark:border-neutral-700 dark:bg-neutral-900\">");
        w.Write("<div class=\"text-xs font-semibold tracking-wide text-neutral-500 uppercase\" x-text=\"editingLabel\"></div>");

        if (IsListMode(column))
        {
            AppendValueListBody(w, column);
        }
        else
        {
            AppendOperatorBody(w, column);
        }

        w.Write("<div class=\"flex justify-end gap-2 pt-1\">");
        w.Write("<button type=\"button\" class=\"btn-ghost-sm\" @click=\"clearFilter(editing); cancelEditing()\">");
        w.Write(L("filter.clear"));
        w.Write("</button>");
        w.Write("<button type=\"button\" class=\"btn-primary-sm\" @click=\"applyFilter()\">");
        w.Write(L("filter.apply"));
        w.Write("</button>");
        w.Write("</div></div>");
    }

    private static bool IsListMode(GridColumn<TItem> column) =>
        column.DataType is ColumnDataType.Text or ColumnDataType.Enum or ColumnDataType.Boolean;

    private void AppendValueListBody(TextWriter w, GridColumn<TItem> column)
    {
        w.Write("<input type=\"search\" x-model=\"listSearch\" placeholder=\"");
        w.Write(L("filter.values.search"));
        w.Write("\" class=\"input-base\">");

        w.Write("<label class=\"flex cursor-pointer items-center gap-2 text-sm font-medium\">");
        w.Write("<input type=\"checkbox\" class=\"h-4 w-4 accent-brand-600\" :checked=\"listAllSelected(");
        AppendJsQuoted(w, column.Field);
        w.Write(")\" @change=\"toggleListAll(");
        AppendJsQuoted(w, column.Field);
        w.Write(", $event.target.checked)\">");
        w.Write("<span>");
        w.Write(L("filter.values.selectAll"));
        w.Write("</span>");
        w.Write("</label>");

        w.Write("<div class=\"max-h-56 space-y-1 overflow-y-auto pr-1\">");
        w.Write("<div x-show=\"!listItems[");
        AppendJsQuoted(w, column.Field);
        w.Write("]\" class=\"text-sm text-neutral-400\">");
        w.Write(L("filter.values.loading"));
        w.Write("</div>");
        w.Write("<template x-for=\"item in visibleListItems(");
        AppendJsQuoted(w, column.Field);
        w.Write(")\" :key=\"item.value\">");
        w.Write("<label class=\"flex cursor-pointer items-center gap-2 py-0.5 text-sm\">");
        w.Write("<input type=\"checkbox\" class=\"h-4 w-4 accent-brand-600\" :checked=\"item.checked\" @change=\"item.checked = !item.checked\">");
        w.Write("<span class=\"flex-1 truncate\" x-text=\"item.value\"></span>");
        w.Write("<span class=\"text-xs tabular-nums text-neutral-400\" x-text=\"item.count\"></span>");
        w.Write("</label></template>");
        w.Write("<div x-show=\"listTruncated(");
        AppendJsQuoted(w, column.Field);
        w.Write(")\" class=\"text-xs text-neutral-400\" x-text=\"listTruncatedLabel(");
        AppendJsQuoted(w, column.Field);
        w.Write(")\"></div>");
        w.Write("</div>");
    }

    private void AppendOperatorBody(TextWriter w, GridColumn<TItem> column)
    {
        w.Write("<select x-model=\"editingOp\" class=\"input-base\">");

        foreach (var op in FilterOperatorMapper.ToOperators(column.AllowedOps))
        {
            if (op == FilterOperator.In)
            {
                continue;
            }

            w.Write("<option value=\"");
            AppendEncoded(w, FilterOperatorMapper.ToToken(op));
            w.Write("\">");
            AppendEncoded(w, L("ops." + FilterOperatorMapper.ToToken(op)));
            w.Write("</option>");
        }

        w.Write("</select>");
        w.Write("<input type=\"text\" x-show=\"!isEmptyOp(editingOp)\" x-model=\"editingValue\" @keydown.enter.prevent=\"applyFilter()\" placeholder=\"");
        w.Write(L("filter.value.placeholder"));
        w.Write("\" class=\"input-base\">");
    }

    private void AppendPager(TextWriter w)
    {
        w.Write("<div class=\"flex items-center justify-between text-sm\">");
        w.Write("<span class=\"text-neutral-500 dark:text-neutral-400\" x-text=\"rangeLabel\"></span>");
        w.Write("<div class=\"flex items-center gap-2\">");
        w.Write("<select x-model.number=\"pageSize\" @change=\"onPageSize()\" aria-label=\"");
        w.Write(L("pager.rowsPerPage"));
        w.Write("\" class=\"input-base !w-auto py-1.5\">");

        foreach (var choice in _options.PageSizeChoices)
        {
            w.Write("<option value=\"");
            w.Write(choice.ToString(CultureInfo.InvariantCulture));
            w.Write("\">");
            w.Write(choice.ToString(CultureInfo.InvariantCulture));
            w.Write("</option>");
        }

        w.Write("</select>");
        w.Write("<button type=\"button\" class=\"btn-ghost\" :disabled=\"page <= 1\" @click=\"go(-1)\">");
        w.Write(L("pager.prev"));
        w.Write("</button>");
        w.Write("<span class=\"px-1 tabular-nums\" x-text=\"page + ' / ' + totalPages\"></span>");
        w.Write("<button type=\"button\" class=\"btn-ghost\" :disabled=\"page >= totalPages\" @click=\"go(1)\">");
        w.Write(L("pager.next"));
        w.Write("</button>");
        w.Write("</div></div>");
    }

    private void AppendSelectionBar(TextWriter w)
    {
        w.Write("<div x-show=\"selected.length > 0\" x-cloak x-transition ");
        w.Write("class=\"fixed bottom-6 left-1/2 z-40 flex -translate-x-1/2 items-center gap-4 rounded-full bg-neutral-900 px-5 py-2.5 text-white shadow-lg dark:bg-white dark:text-neutral-900\">");
        w.Write("<span class=\"text-sm font-medium\" x-text=\"selectedLabel\"></span>");
        w.Write("<form method=\"post\" :action=\"'/netgrid/' + id + '/export'\" class=\"contents\">");
        w.Write("<input type=\"hidden\" name=\"ids\" :value=\"selected.join(',')\">");
        w.Write("<button type=\"submit\" class=\"btn-primary-sm\">");
        w.Write(L("select.export"));
        w.Write("</button>");
        w.Write("</form>");
        w.Write("<button type=\"button\" class=\"text-sm underline underline-offset-2 opacity-80 hover:opacity-100\" @click=\"clearSelection()\">");
        w.Write(L("select.clear"));
        w.Write("</button>");
        w.Write("</div>");
    }

    private void AppendRows(TextWriter w, PageResult<TItem> page, IReadOnlyList<GridColumn<TItem>> columns)
    {
        if (page.Items.Count == 0)
        {
            w.Write("<tr><td colspan=\"");
            w.Write((columns.Count + (_options.EnableRowSelection ? 1 : 0)).ToString(CultureInfo.InvariantCulture));

            if (_options.MinHeight.Length > 0)
            {
                w.Write("\" style=\"height:");
                AppendEncoded(w, _options.MinHeight);
            }

            w.Write("\" class=\"px-4 py-12 text-center text-sm text-neutral-500 align-middle dark:text-neutral-400\">");
            AppendEncoded(w, _options.EmptyMessage);
            w.Write("</td></tr>");
            return;
        }

        foreach (var item in page.Items)
        {
            AppendDataRow(w, item, columns);
        }
    }

    private void AppendDataRow(TextWriter w, TItem item, IReadOnlyList<GridColumn<TItem>> columns)
    {
        var rowKey = _options.EnableRowSelection ? _options.RowKey?.Invoke(item) : null;

        w.Write("<tr class=\"border-b border-neutral-100 transition-colors last:border-0 hover:bg-brand-50/40 dark:border-neutral-800/60 dark:hover:bg-white/[0.04]\"");

        if (_options.EnableRowSelection && rowKey is not null)
        {
            w.Write(" data-id=\"");
            AppendEncoded(w, rowKey);
            w.Write('"');
        }

        w.Write('>');

        if (_options.EnableRowSelection && rowKey is not null)
        {
            w.Write("<td data-pin=\"__select\" style=\"left:0\" class=\"sticky left-0 z-10 w-10 border-r border-neutral-100 bg-white px-4 py-2 align-middle hover:bg-brand-50/40 dark:border-neutral-800/60 dark:bg-neutral-900 dark:hover:bg-white/[0.04]\">");
            w.Write("<input type=\"checkbox\" class=\"h-4 w-4 accent-brand-600\" aria-label=\"");
            w.Write(L("select.row.aria"));
            w.Write("\" @change=\"toggleSelection($el.closest('tr').dataset.id)\" :checked=\"isSelected(");
            AppendJsQuoted(w, rowKey);
            w.Write(")\">");
            w.Write("</td>");
        }

        foreach (var column in columns)
        {
            w.Write("<td data-field=\"");
            AppendEncoded(w, column.Field);
            w.Write("\"");

            if (column.IsPinned)
            {
                w.Write(" data-pin=\"");
                AppendEncoded(w, column.Field);
                w.Write("\" style=\"left:0\" class=\"sticky z-10 border-r border-neutral-100 bg-white hover:bg-brand-50/40 dark:border-neutral-800/60 dark:bg-neutral-900 dark:hover:bg-white/[0.04]");
            }
            else
            {
                w.Write(" class=\"");
            }

            w.Write(" whitespace-nowrap px-4 py-2.5 align-middle ");
            w.Write(TextAlignClass(column.Align));
            w.Write("\">");

            if (column.RawCellHtml is { } rawCell)
            {
                w.Write(rawCell(item));
            }
            else
            {
                AppendEncoded(w, column.Format(item));
            }

            w.Write("</td>");
        }

        w.Write("</tr>");
    }

    private void AppendStateScript(TextWriter w, PageResult<TItem> page)
    {
        w.Write("<script>window.__NETGRID__=window.__NETGRID__||{};");
        w.Write("__NETGRID__.initial=__NETGRID__.initial||{};");
        w.Write("__NETGRID__.columns=__NETGRID__.columns||{};");

        w.Write("__NETGRID__.initial[");
        AppendJsonString(w, _options.Id);
        w.Write("]={\"total\":");
        w.Write(page.TotalCount.ToString(CultureInfo.InvariantCulture));
        w.Write(",\"page\":");
        w.Write(page.Page.ToString(CultureInfo.InvariantCulture));
        w.Write(",\"pageSize\":");
        w.Write(page.PageSize.ToString(CultureInfo.InvariantCulture));
        w.Write(",\"pages\":");
        w.Write(Math.Max(page.TotalPages, 1).ToString(CultureInfo.InvariantCulture));
        w.Write("};");

        w.Write("__NETGRID__.columns[");
        AppendJsonString(w, _options.Id);
        w.Write("]=[");

        for (var i = 0; i < _visibleColumns.Count; i++)
        {
            if (i > 0)
            {
                w.Write(',');
            }

            w.Write("{\"field\":");
            AppendJsonString(w, _visibleColumns[i].Field);
            w.Write(",\"header\":");
            AppendJsonString(w, _visibleColumns[i].Header);
            w.Write(",\"flipX\":\"");
            w.Write(IsFirstFilterable(_visibleColumns[i]) ? "left" : "right");
            w.Write("\",\"mode\":\"");
            w.Write(_visibleColumns[i].IsFilterable && IsListMode(_visibleColumns[i]) ? "list" : "op");
            w.Write("\",\"pin\":");
            w.Write(_visibleColumns[i].IsPinned ? "true" : "false");
            w.Write("}");
        }

        w.Write("];");
        w.Write("__NETGRID__.locale=");
        w.Write(JsonSerializer.Serialize(_locale.Strings, JsonOptions));
        w.Write(";</script>");
    }

    private bool IsFirstFilterable(GridColumn<TItem> column)
    {
        foreach (var candidate in _visibleColumns)
        {
            if (candidate.IsFilterable)
            {
                return candidate.Field == column.Field;
            }
        }

        return false;
    }

    private static string TextAlignClass(ColumnAlign align) => align switch
    {
        ColumnAlign.Center => "text-center",
        ColumnAlign.End => "text-right",
        _ => "text-left"
    };

    private static void AppendEncoded(TextWriter w, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            Encoder.Encode(w, value, 0, value.Length);
        }
    }

    /// <summary>Escapes a literal destined for a single-quoted JS expression inside an HTML attribute.</summary>
    private static void AppendJsQuoted(TextWriter w, string? value)
    {
        w.Write('\'');
        if (!string.IsNullOrEmpty(value))
        {
            var safe = value.Replace("\\", "\\\\").Replace("'", "\\'");
            AppendEncoded(w, safe);
        }

        w.Write('\'');
    }

    private static void AppendJsonString(TextWriter w, string value)
    {
        w.Write('"');
        w.Write(JsonEncodedText.Encode(value).ToString());
        w.Write('"');
    }
}
