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

    private const string ColumnsIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\" fill=\"currentColor\" class=\"h-4 w-4\"><path d=\"M3 4a1 1 0 0 1 1-1h2.5a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4Zm5.75 0a1 1 0 0 1 1-1h.5a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1h-.5a1 1 0 0 1-1-1V4Zm4.75 0a1 1 0 0 1 1-1H16a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1h-1.5a1 1 0 0 1-1-1V4Z\"/></svg>";

    private const string BookmarkIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\" fill=\"currentColor\" class=\"h-4 w-4\"><path fill-rule=\"evenodd\" d=\"M10 2c-1.716 0-3.408.106-5.07.31C3.806 2.45 3 3.414 3 4.517V17.25a.75.75 0 0 0 1.075.676L10 15.082l5.925 2.844A.75.75 0 0 0 17 17.25V4.517c0-1.103-.806-2.068-1.93-2.207A41.403 41.403 0 0 0 10 2Z\" clip-rule=\"evenodd\"/></svg>";

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
        _visibleColumns = options.DisplayColumns;
        _stringBuilderPool = new DefaultObjectPoolProvider()
            .CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 256 * 1024);
    }

    private string L(string key) => _locale[key];

    public ValueTask<string> RenderRowsAsync(
        GridExecutionResult<TItem> result,
        IReadOnlyList<GridColumn<TItem>>? columnOrder = null,
        CancellationToken cancellationToken = default,
        GridAggregates? totals = null,
        IReadOnlySet<string>? hiddenFields = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var columns = BodyColumns(ResolveColumns(columnOrder), hiddenFields);
        var sb = _stringBuilderPool.Get();
        try
        {
            using var writer = new StringWriter(sb);
            AppendRows(writer, result.Page, columns, totals);
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
        CancellationToken cancellationToken = default,
        GridAggregates? totals = null,
        IReadOnlySet<string>? hiddenFields = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var columns = BodyColumns(ResolveColumns(columnOrder), hiddenFields);
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
                AppendTotalsRow(writer, GridAggregateRows.Header, totals, columns);

                foreach (var group in grouped.Groups)
                {
                    AppendGroup(writer, group, columns);
                }

                AppendTotalsRow(writer, GridAggregateRows.Footer, totals, columns);
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
        var inlineAggregates = _options.ShowsAggregates(GridAggregateRows.GroupHeader) &&
                               !group.Aggregates.IsEmpty &&
                               LeadingLabelSpan(columns) > 0;

        w.Write("<tr class=\"group-header bg-neutral-50/80 dark:bg-neutral-800/40\" data-group-path=\"");
        AppendEncoded(w, group.Path);
        w.Write("\">");

        if (inlineAggregates)
        {
            AppendAggregateCells(w, columns, group.Aggregates, "py-2", () => AppendGroupLabel(w, group, expanded));
        }
        else
        {
            w.Write("<td colspan=\"");
            w.Write(ColumnSpan(columns).ToString(CultureInfo.InvariantCulture));
            w.Write("\" class=\"px-4 py-2\">");
            AppendGroupLabel(w, group, expanded);
            w.Write("</td>");
        }

        w.Write("</tr>");

        // Group-header subtotals that could not share the label's row (the first column is itself
        // aggregated and there is no selection column to hold the label) get a row of their own.
        if (!inlineAggregates && _options.ShowsAggregates(GridAggregateRows.GroupHeader) && !group.Aggregates.IsEmpty)
        {
            w.Write("<tr class=\"agg-row agg-subtotal bg-neutral-50/80 text-xs dark:bg-neutral-800/40\">");
            AppendAggregateCells(w, columns, group.Aggregates, "py-1.5", null);
            w.Write("</tr>");
        }

        foreach (var child in group.Children)
        {
            AppendGroup(w, child, columns);
        }

        foreach (var row in group.Rows)
        {
            AppendDataRow(w, row, columns);
        }

        if (expanded && _options.ShowsAggregates(GridAggregateRows.GroupFooter) && !group.Aggregates.IsEmpty)
        {
            w.Write("<tr class=\"agg-row agg-subtotal border-b border-neutral-200 bg-neutral-50/40 dark:border-neutral-800 dark:bg-neutral-800/20\" data-group-footer=\"");
            AppendEncoded(w, group.Path);
            w.Write("\">");
            AppendAggregateCells(w, columns, group.Aggregates, "py-2", () =>
            {
                w.Write("<div class=\"text-xs font-medium text-neutral-500 dark:text-neutral-400\" style=\"padding-left:");
                w.Write(GroupIndent(group.Level).ToString(CultureInfo.InvariantCulture));
                w.Write("px\">");
                AppendEncoded(w, L("agg.subtotal"));
                w.Write(' ');
                AppendEncoded(w, GroupLabel(group));
                w.Write("</div>");
            });
            w.Write("</tr>");
        }
    }

    private string GroupLabel(GridGroup<TItem> group) => group.Value.Length == 0 ? L("group.blank") : group.Value;

    private static int GroupIndent(int level) => 8 + level * 20;

    private void AppendGroupLabel(TextWriter w, GridGroup<TItem> group, bool expanded)
    {
        w.Write("<div class=\"flex items-center gap-2\" style=\"padding-left:");
        w.Write(GroupIndent(group.Level).ToString(CultureInfo.InvariantCulture));
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
        AppendEncoded(w, GroupLabel(group));
        w.Write("</span>");

        w.Write("<span class=\"badge badge-muted tabular-nums\">");
        w.Write(group.Count.ToString(CultureInfo.InvariantCulture));
        w.Write("</span>");

        w.Write("</div>");
    }

    /// <summary>Grand-total row ("Total") for the table header or footer position.</summary>
    private void AppendTotalsRow(TextWriter w, GridAggregateRows position, GridAggregates? totals, IReadOnlyList<GridColumn<TItem>> columns)
    {
        if (totals is null || totals.IsEmpty || !_options.ShowsAggregates(position))
        {
            return;
        }

        w.Write(position == GridAggregateRows.Header
            ? "<tr class=\"agg-row agg-total agg-total-header border-b-2 border-neutral-200 bg-neutral-50 dark:border-neutral-700 dark:bg-neutral-800/60\">"
            : "<tr class=\"agg-row agg-total agg-total-footer border-t-2 border-neutral-200 bg-neutral-50 dark:border-neutral-700 dark:bg-neutral-800/60\">");

        AppendAggregateCells(w, columns, totals, "py-2.5", () =>
        {
            w.Write("<span class=\"text-xs font-semibold uppercase tracking-wide text-neutral-500 dark:text-neutral-400\">");
            AppendEncoded(w, L("agg.total"));
            w.Write("</span>");
        });

        w.Write("</tr>");
    }

    /// <summary>
    /// Leading columns without aggregates collapse into one label cell (plus the selection column);
    /// from the first aggregated column on, cells line up with the data columns so each value sits
    /// under its own header. Returns nothing when there is no room for a label: the caller passes
    /// <paramref name="label"/> null and every column gets its own cell.
    /// </summary>
    private void AppendAggregateCells(
        TextWriter w,
        IReadOnlyList<GridColumn<TItem>> columns,
        GridAggregates aggregates,
        string paddingY,
        Action? label)
    {
        var span = label is null ? 0 : LeadingLabelSpan(columns);
        var selection = _options.EnableRowSelection ? 1 : 0;

        if (span > 0)
        {
            w.Write("<td colspan=\"");
            w.Write(span.ToString(CultureInfo.InvariantCulture));
            w.Write("\" class=\"px-4 ");
            w.Write(paddingY);
            w.Write(" align-middle\">");
            label!();
            w.Write("</td>");
        }
        else if (selection > 0)
        {
            w.Write("<td></td>");
        }

        var skip = Math.Max(span - selection, 0);
        for (var i = skip; i < columns.Count; i++)
        {
            var column = columns[i];
            w.Write("<td data-field=\"");
            AppendEncoded(w, column.Field);
            w.Write("\" class=\"");
            w.Write(ResponsiveClass(column.HideBelow));
            w.Write("whitespace-nowrap px-4 ");
            w.Write(paddingY);
            w.Write(" align-top ");
            w.Write(TextAlignClass(column.Align));
            w.Write("\">");

            if (column.HasAggregates && aggregates.For(column.Field) is { } values)
            {
                AppendAggregateValues(w, column, values);
            }

            w.Write("</td>");
        }

        if (_options.HasRowActions)
        {
            // Under a pinned actions column the empty cell must stick too, or the scrolled
            // columns show through it.
            w.Write(_options.RowActionsPinned
                ? "<td data-pin-right=\"__actions\" style=\"right:0\" class=\"sticky z-10 border-l border-neutral-200 bg-neutral-50 dark:border-neutral-800 dark:bg-[color:color-mix(in_oklab,var(--color-neutral-800)_60%,var(--color-neutral-900))]\"></td>"
                : "<td></td>");
        }
    }

    private void AppendAggregateValues(TextWriter w, GridColumn<TItem> column, GridAggregateValues values)
    {
        foreach (var function in column.Aggregates.Functions())
        {
            w.Write("<div class=\"leading-5\" data-agg=\"");
            w.Write(AggregateKey(function));
            w.Write("\"><span class=\"mr-1.5 text-[10px] font-medium uppercase tracking-wide text-neutral-400 dark:text-neutral-500\">");
            AppendEncoded(w, L("agg." + AggregateKey(function)));
            w.Write("</span><span class=\"font-semibold tabular-nums text-neutral-800 dark:text-neutral-100\">");

            if (values.Get(function) is { } value)
            {
                AppendEncoded(w, column.AggregateFormat?.Invoke(function, value) ?? value.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                w.Write("&mdash;");
            }

            w.Write("</span></div>");
        }
    }

    private static string AggregateKey(GridAggregate function) => function switch
    {
        GridAggregate.Sum => "sum",
        GridAggregate.Avg => "avg",
        GridAggregate.Min => "min",
        _ => "max"
    };

    /// <summary>Selection column + leading non-aggregated columns: where a row label can go.</summary>
    private int LeadingLabelSpan(IReadOnlyList<GridColumn<TItem>> columns)
    {
        var leading = 0;
        while (leading < columns.Count && !columns[leading].HasAggregates)
        {
            leading++;
        }

        return leading + (_options.EnableRowSelection ? 1 : 0);
    }

    /// <summary>Every rendered column: selection + data columns + the trailing actions column.</summary>
    private int ColumnSpan(IReadOnlyList<GridColumn<TItem>> columns) =>
        columns.Count + (_options.EnableRowSelection ? 1 : 0) + (_options.HasRowActions ? 1 : 0);

    private void AppendEmptyRow(TextWriter w, IReadOnlyList<GridColumn<TItem>> columns)
    {
        w.Write("<tr><td colspan=\"");
        w.Write(ColumnSpan(columns).ToString(CultureInfo.InvariantCulture));
        w.Write("\" class=\"px-4 py-12 text-center text-sm text-neutral-500 dark:text-neutral-400\">");
        AppendEncoded(w, _options.EmptyMessage);
        w.Write("</td></tr>");
    }

    /// <summary>
    /// Columns the body renders: the resolved order minus the ones the user hid. The header keeps
    /// every column (hidden ones carry the <c>hidden</c> attribute) because a rows refresh only
    /// swaps the &lt;tbody&gt;, so showing a column again must not need a new header.
    /// </summary>
    private static IReadOnlyList<GridColumn<TItem>> BodyColumns(IReadOnlyList<GridColumn<TItem>> ordered, IReadOnlySet<string>? hiddenFields)
    {
        if (hiddenFields is null || hiddenFields.Count == 0)
        {
            return ordered;
        }

        var visible = ordered.Where(c => !hiddenFields.Contains(c.Field)).ToArray();
        return visible.Length > 0 ? visible : ordered;
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

    /// <summary>
    /// Renders just the grid - root element plus its state script, and no document scaffolding.
    /// Meant to be dropped inside a host page's own layout, which renders
    /// <see cref="GridAssetTags"/> once in its own &lt;head&gt; instead.
    /// </summary>
    /// <param name="initialResult">Resultado de la consulta inicial que se pinta en el primer render.</param>
    /// <param name="columnOrder">
    /// Orden de columnas elegido por el usuario. Si es <see langword="null"/> o esta vacio se usa
    /// el orden configurado; las columnas visibles ausentes se anaden al final.
    /// </param>
    /// <param name="cancellationToken">Token para cancelar el render.</param>
    /// <param name="totals">
    /// Agregados del conjunto filtrado completo para las filas de totales; <see langword="null"/>
    /// si el grid no las muestra.
    /// </param>
    /// <param name="hiddenFields">
    /// Columnas que el usuario oculto: el encabezado las conserva con el atributo <c>hidden</c> y el
    /// cuerpo no las pinta.
    /// </param>
    /// <returns>El HTML del grid, sin &lt;html&gt; ni &lt;head&gt;.</returns>
    public ValueTask<string> RenderFragmentAsync(
        GridExecutionResult<TItem> initialResult,
        IReadOnlyList<GridColumn<TItem>>? columnOrder = null,
        CancellationToken cancellationToken = default,
        GridAggregates? totals = null,
        IReadOnlySet<string>? hiddenFields = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sb = _stringBuilderPool.Get();
        try
        {
            using var writer = new StringWriter(sb);
            AppendGridRoot(writer, initialResult, ResolveColumns(columnOrder), totals, hiddenFields);
            AppendStateScript(writer, initialResult.Page);
            return ValueTask.FromResult(sb.ToString());
        }
        finally
        {
            _stringBuilderPool.Return(sb.Clear());
        }
    }

    /// <summary>
    /// Renderiza el documento HTML completo del grid (shell): cabecera, toolbar, tabla con la
    /// primera pagina ya pintada y los scripts embebidos que activan HTMX y Alpine.
    ///
    /// <para>El cuerpo lo produce <see cref="RenderFragmentAsync"/>; el shell solo le pone
    /// alrededor el documento y el cromo. Asi las dos salidas no pueden divergir.</para>
    /// </summary>
    /// <param name="initialResult">Resultado de la consulta inicial que se pinta en el primer render.</param>
    /// <param name="columnOrder">
    /// Orden de columnas elegido por el usuario. Si es <see langword="null"/> o esta vacio se usa
    /// el orden configurado; las columnas visibles ausentes se anaden al final.
    /// </param>
    /// <param name="embedded">
    /// El grid se pinta dentro de un iframe en otra pagina. Entonces sobra su propio cromo: el
    /// fondo de pagina, el ancho maximo y el relleno los pone ya la pagina anfitriona, y
    /// repetirlos deja la tabla encajonada y pequena.
    /// </param>
    /// <param name="cancellationToken">Token para cancelar el render.</param>
    /// <param name="totals">
    /// Agregados del conjunto filtrado completo para las filas de totales; <see langword="null"/>
    /// si el grid no las muestra.
    /// </param>
    /// <param name="hiddenFields">
    /// Columnas que el usuario oculto: el encabezado las conserva con el atributo <c>hidden</c> y el
    /// cuerpo no las pinta.
    /// </param>
    /// <returns>El HTML del shell como cadena.</returns>
    public async ValueTask<string> RenderShellAsync(
        GridExecutionResult<TItem> initialResult,
        IReadOnlyList<GridColumn<TItem>>? columnOrder = null,
        bool embedded = false,
        CancellationToken cancellationToken = default,
        GridAggregates? totals = null,
        IReadOnlySet<string>? hiddenFields = null)
    {
        var fragment = await RenderFragmentAsync(initialResult, columnOrder, cancellationToken, totals, hiddenFields);

        var sb = _stringBuilderPool.Get();
        try
        {
            using var writer = new StringWriter(sb);
            AppendDocumentStart(writer, embedded);
            if (!embedded)
            {
                AppendSiteHeader(writer);
            }

            writer.Write(embedded
                ? "<main>"
                : "<main class=\"mx-auto max-w-7xl px-4 py-8\">");
            writer.Write(fragment);
            writer.Write("</main>");
            writer.Write("</body></html>");

            return sb.ToString();
        }
        finally
        {
            _stringBuilderPool.Return(sb.Clear());
        }
    }

    private void AppendDocumentStart(TextWriter w, bool embedded)
    {
        var prefix = _assetOptions.NormalizedAssetPrefix;

        w.Write("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        w.Write("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        w.Write("<title>");
        AppendEncoded(w, _options.Title);
        w.Write("</title>");
        w.Write(ThemeBootScript);
        w.Write("<link rel=\"stylesheet\" href=\"");
        if (_assetOptions.NormalizedCssPath is { } cssPath)
        {
            w.Write(cssPath);
            w.Write('/');
            w.Write(_assetOptions.CssFilePrefix);
            AppendEncoded(w, _options.Theme);
            w.Write(".css");
        }
        else
        {
            w.Write(prefix);
            w.Write("/css/netopengrid-");
            AppendEncoded(w, _options.Theme);
            w.Write(".css");

            if (Assets.EmbeddedGridAssets.Themes.TryGetValue(_options.Theme, out var themeAsset))
            {
                w.Write("?v=");
                w.Write(themeAsset.Version);
            }
        }
        w.Write("\">");
        w.Write("<style>[x-cloak]{display:none!important}</style>");
        if (embedded)
        {
            w.Write("<style>html,body{background:transparent!important;margin:0}</style>");
        }

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
        w.Write(embedded
            ? "</head><body class=\"text-neutral-900 antialiased dark:text-neutral-100\">"
            : "</head><body class=\"min-h-screen bg-neutral-100 text-neutral-900 antialiased dark:bg-neutral-950 dark:text-neutral-100\">");
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

    private void AppendGridRoot(TextWriter w, GridExecutionResult<TItem> initialResult, IReadOnlyList<GridColumn<TItem>> columns, GridAggregates? totals, IReadOnlySet<string>? hiddenFields)
    {
        w.Write("<div id=\"");
        AppendEncoded(w, _options.Id);
        w.Write("\" x-data=\"netgrid(");
        AppendJsQuoted(w, _options.Id);
        w.Write(")\" class=\"space-y-4\">");

        AppendToolbar(w);
        AppendFilterChips(w);
        AppendTableCard(w, initialResult, columns, totals, hiddenFields);
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
            AppendEncoded(w, column.Label);
            w.Write("</option>");
        }

        w.Write("</select>");

        if (_options.EnableSavedViews || _options.Views.Count > 0)
        {
            AppendViewsMenu(w);
        }

        if (_options.EnableColumnChooser)
        {
            AppendColumnChooser(w);
        }

        w.Write("<button type=\"button\" @click=\"toggleTheme()\" class=\"btn-icon\" aria-label=\"");
        w.Write(L("theme.aria"));
        w.Write("\">");
        w.Write(SunIcon);
        w.Write(MoonIcon);
        w.Write("</button>");

        AppendExportControl(w);

        w.Write("<span x-show=\"loading\" x-cloak aria-hidden=\"true\">");
        w.Write(SpinnerIcon);
        w.Write("</span>");
        w.Write("</div>");
    }

    /// <summary>
    /// Export button: nothing when every format is off, a direct button for a single format, and a
    /// small menu (CSV / Excel) when both are on.
    /// </summary>
    private void AppendExportControl(TextWriter w)
    {
        var formats = _options.ExportFormats & GridExportFormats.All;
        if (formats == GridExportFormats.None)
        {
            return;
        }

        if (formats != GridExportFormats.All)
        {
            w.Write("<button type=\"button\" @click=\"exportAs(");
            w.Write(formats == GridExportFormats.Xlsx ? "'xlsx'" : "'csv'");
            w.Write(")\" class=\"btn-icon\" aria-label=\"");
            w.Write(L("export.aria"));
            w.Write("\">");
            w.Write(DownloadIcon);
            w.Write("</button>");
            return;
        }

        w.Write("<div class=\"relative\" @click.outside=\"exportMenu = false\" @keydown.escape=\"exportMenu = false\">");
        w.Write("<button type=\"button\" class=\"btn-icon\" @click=\"exportMenu = !exportMenu\" :aria-expanded=\"exportMenu\" aria-haspopup=\"true\" aria-controls=\"");
        AppendEncoded(w, _options.Id);
        w.Write("-export\" aria-label=\"");
        w.Write(L("export.aria"));
        w.Write("\">");
        w.Write(DownloadIcon);
        w.Write("</button>");
        w.Write("<div id=\"");
        AppendEncoded(w, _options.Id);
        w.Write("-export\" x-show=\"exportMenu\" x-cloak class=\"absolute right-0 top-full z-40 mt-2 w-44 space-y-0.5 rounded-xl border border-neutral-200 bg-white p-2 shadow-xl dark:border-neutral-700 dark:bg-neutral-900\">");

        foreach (var (format, key) in new[] { ("csv", "export.csv"), ("xlsx", "export.xlsx") })
        {
            w.Write("<button type=\"button\" class=\"flex w-full cursor-pointer items-center rounded-md px-2 py-1.5 text-left text-sm hover:bg-neutral-100 dark:hover:bg-neutral-800\" @click=\"exportMenu = false; exportAs('");
            w.Write(format);
            w.Write("')\">");
            w.Write(L(key));
            w.Write("</button>");
        }

        w.Write("</div></div>");
    }

    /// <summary>
    /// Views menu: "default view", the server's predefined views and (when enabled) the user's own,
    /// saved in the browser. Names are rendered with x-text, never as HTML.
    /// </summary>
    private void AppendViewsMenu(TextWriter w)
    {
        const string item = "flex min-w-0 flex-1 cursor-pointer items-center gap-2 rounded-md px-1.5 py-1 text-left text-sm hover:bg-neutral-100 dark:hover:bg-neutral-800";
        const string check = "<span class=\"w-3 shrink-0 text-brand-600 dark:text-brand-400\" aria-hidden=\"true\" x-text=\"isActiveView({0}) ? '✓' : ''\"></span>";
        const string heading = "px-1.5 pt-2 pb-0.5 text-[11px] font-semibold tracking-wide text-neutral-400 uppercase";

        w.Write("<div class=\"relative\" @click.outside=\"viewsMenu = false\" @keydown.escape=\"viewsMenu = false\">");
        w.Write("<button type=\"button\" class=\"btn-icon\" @click=\"viewsMenu = !viewsMenu\" :aria-expanded=\"viewsMenu\" aria-haspopup=\"true\" aria-controls=\"");
        AppendEncoded(w, _options.Id);
        w.Write("-views\" aria-label=\"");
        w.Write(L("views.aria"));
        w.Write("\" :class=\"activeViewName() ? 'text-brand-600 dark:text-brand-400' : ''\">");
        w.Write(BookmarkIcon);
        w.Write("</button>");

        w.Write("<div id=\"");
        AppendEncoded(w, _options.Id);
        w.Write("-views\" x-show=\"viewsMenu\" x-cloak class=\"absolute right-0 top-full z-40 mt-2 max-h-[70vh] w-64 overflow-y-auto rounded-xl border border-neutral-200 bg-white p-2 shadow-xl dark:border-neutral-700 dark:bg-neutral-900\">");
        w.Write("<div class=\"px-1.5 pb-1 text-xs font-semibold tracking-wide text-neutral-500 uppercase\">");
        w.Write(L("views.title"));
        w.Write("</div>");

        w.Write("<button type=\"button\" class=\"");
        w.Write(item);
        w.Write(" w-full\" @click=\"applyView('')\">");
        w.Write(string.Format(CultureInfo.InvariantCulture, check, "''"));
        w.Write("<span>");
        w.Write(L("views.default"));
        w.Write("</span></button>");

        if (_options.Views.Count > 0)
        {
            w.Write("<div class=\"");
            w.Write(heading);
            w.Write("\">");
            w.Write(L("views.shared"));
            w.Write("</div>");
            w.Write("<template x-for=\"view in serverViews\" :key=\"'s:' + view.name\">");
            w.Write("<button type=\"button\" class=\"");
            w.Write(item);
            w.Write(" w-full\" @click=\"applyView(view.query)\">");
            w.Write(string.Format(CultureInfo.InvariantCulture, check, "view.query"));
            w.Write("<span class=\"truncate\" x-text=\"view.name\"></span></button>");
            w.Write("</template>");
        }

        if (_options.EnableSavedViews)
        {
            w.Write("<div class=\"");
            w.Write(heading);
            w.Write("\">");
            w.Write(L("views.mine"));
            w.Write("</div>");
            w.Write("<p x-show=\"userViews.length === 0\" class=\"px-1.5 py-1 text-xs text-neutral-500 dark:text-neutral-400\">");
            w.Write(L("views.empty"));
            w.Write("</p>");
            w.Write("<template x-for=\"view in userViews\" :key=\"'u:' + view.name\">");
            w.Write("<div class=\"flex items-center gap-1\">");
            w.Write("<button type=\"button\" class=\"");
            w.Write(item);
            w.Write("\" @click=\"applyView(view.query)\">");
            w.Write(string.Format(CultureInfo.InvariantCulture, check, "view.query"));
            w.Write("<span class=\"truncate\" x-text=\"view.name\"></span></button>");
            w.Write("<button type=\"button\" class=\"cursor-pointer rounded-md px-1.5 py-1 text-neutral-400 hover:bg-neutral-100 hover:text-red-600 dark:hover:bg-neutral-800 dark:hover:text-red-400\" :aria-label=\"deleteViewLabel(view.name)\" @click=\"deleteView(view.name)\">&times;</button>");
            w.Write("</div></template>");

            w.Write("<form class=\"mt-2 flex gap-1.5 border-t border-neutral-100 pt-2 dark:border-neutral-800\" @submit.prevent=\"saveCurrentView()\">");
            w.Write("<input type=\"text\" class=\"input-base !py-1\" maxlength=\"60\" x-model=\"viewName\" placeholder=\"");
            w.Write(L("views.placeholder"));
            w.Write("\" aria-label=\"");
            w.Write(L("views.placeholder"));
            w.Write("\">");
            w.Write("<button type=\"submit\" class=\"btn-primary-sm\" :disabled=\"!viewName.trim()\">");
            w.Write(L("views.save"));
            w.Write("</button></form>");
        }

        w.Write("</div></div>");
    }

    /// <summary>Toolbar menu: one checkbox per column; the last visible one cannot be unchecked.</summary>
    private void AppendColumnChooser(TextWriter w)
    {
        w.Write("<div class=\"relative\" @click.outside=\"columnsMenu = false\" @keydown.escape=\"columnsMenu = false\">");
        w.Write("<button type=\"button\" class=\"btn-icon\" @click=\"columnsMenu = !columnsMenu\" :aria-expanded=\"columnsMenu\" aria-haspopup=\"true\" aria-controls=\"");
        AppendEncoded(w, _options.Id);
        w.Write("-columns\" aria-label=\"");
        w.Write(L("columns.aria"));
        w.Write("\">");
        w.Write(ColumnsIcon);
        w.Write("</button>");

        w.Write("<div id=\"");
        AppendEncoded(w, _options.Id);
        w.Write("-columns\" x-show=\"columnsMenu\" x-cloak class=\"absolute right-0 top-full z-40 mt-2 max-h-[70vh] w-60 space-y-0.5 overflow-y-auto rounded-xl border border-neutral-200 bg-white p-2 shadow-xl dark:border-neutral-700 dark:bg-neutral-900\">");
        w.Write("<div class=\"px-1.5 pb-1 text-xs font-semibold tracking-wide text-neutral-500 uppercase\">");
        w.Write(L("columns.title"));
        w.Write("</div>");

        foreach (var column in _visibleColumns)
        {
            w.Write("<label class=\"flex cursor-pointer items-center gap-2 rounded-md px-1.5 py-1 text-sm hover:bg-neutral-100 dark:hover:bg-neutral-800\">");
            w.Write("<input type=\"checkbox\" class=\"h-4 w-4 accent-brand-600\" :checked=\"!isHidden(");
            AppendJsQuoted(w, column.Field);
            w.Write(")\" :disabled=\"!canToggleColumn(");
            AppendJsQuoted(w, column.Field);
            w.Write(")\" @change=\"toggleColumn(");
            AppendJsQuoted(w, column.Field);
            w.Write(")\"><span>");
            AppendEncoded(w, column.Label);
            w.Write("</span></label>");
        }

        w.Write("<div class=\"flex justify-end border-t border-neutral-100 pt-2 dark:border-neutral-800\">");
        w.Write("<button type=\"button\" class=\"btn-ghost-sm\" :disabled=\"hiddenColumns.length === 0\" @click=\"showAllColumns()\">");
        w.Write(L("columns.reset"));
        w.Write("</button></div>");
        w.Write("</div></div>");
    }

    private void AppendFilterChips(TextWriter w)
    {
        if (_options.EnableGroupPanel)
        {
            AppendGroupPanel(w);
        }
        else
        {
            w.Write("<div class=\"flex flex-wrap items-center gap-2\" x-show=\"groupBy.length\" x-cloak>");
            w.Write("<template x-for=\"g in groupBy\" :key=\"g\">");
            w.Write("<button type=\"button\" @click=\"removeGroupBy(g)\" class=\"chip\" title=\"");
            w.Write(L("group.aria"));
            w.Write("\">");
            w.Write("<span x-text=\"columnInfo(g)?.header || g\"></span><span aria-hidden=\"true\">&times;</span>");
            w.Write("</button></template></div>");
        }

        w.Write("<div class=\"flex flex-wrap items-center gap-2\" x-show=\"activeFilters.length\" x-cloak>");
        w.Write("<template x-for=\"entry in activeFilters\" :key=\"entry.field\">");
        w.Write("<button type=\"button\" @click=\"clearFilter(entry.field)\" class=\"chip\" title=\"");
        w.Write(L("chips.remove"));
        w.Write("\">");
        w.Write("<span x-text=\"entry.label\"></span><span aria-hidden=\"true\">&times;</span>");
        w.Write("</button></template></div>");
    }

    /// <summary>
    /// Drop zone for grouping: a header dropped here becomes a group level; chips drag onto each
    /// other to reorder the levels and carry their own remove button. The toolbar's "Group by"
    /// select stays as the keyboard path.
    /// </summary>
    private void AppendGroupPanel(TextWriter w)
    {
        w.Write("<div data-group-panel role=\"group\" aria-label=\"");
        w.Write(L("group.aria"));
        w.Write("\" class=\"flex min-h-10 flex-wrap items-center gap-2 rounded-xl border border-dashed border-neutral-300 px-3 py-1.5 text-sm transition-colors dark:border-neutral-700\"");
        w.Write(" :class=\"groupDropActive ? '!border-brand-500 bg-brand-50/60 dark:!border-brand-400 dark:bg-brand-900/20' : ''\"");
        w.Write(" @dragover.prevent=\"onGroupPanelDragOver($event)\" @dragleave=\"groupDropActive = false\" @drop.prevent=\"onGroupPanelDrop($event)\">");

        w.Write("<span x-show=\"groupBy.length === 0\" class=\"text-neutral-400 select-none dark:text-neutral-500\">");
        w.Write(L("group.panel.hint"));
        w.Write("</span>");

        w.Write("<template x-for=\"(g, i) in groupBy\" :key=\"g\">");
        w.Write("<span class=\"inline-flex items-center gap-2\">");
        w.Write("<span x-show=\"i > 0\" aria-hidden=\"true\" class=\"text-neutral-400\">&rsaquo;</span>");
        w.Write("<span class=\"chip cursor-grab\" draggable=\"true\" :data-group-chip=\"g\" @dragstart=\"onGroupChipDragStart(g, $event)\" @dragover.prevent @drop.prevent.stop=\"onGroupChipDrop(g, $event)\">");
        w.Write("<span x-text=\"columnInfo(g)?.header || g\"></span>");
        w.Write("<button type=\"button\" class=\"cursor-pointer leading-none\" :aria-label=\"removeGroupLabel(g)\" @click=\"removeGroupBy(g)\">&times;</button>");
        w.Write("</span></span></template>");
        w.Write("</div>");
    }

    private void AppendTableCard(TextWriter w, GridExecutionResult<TItem> initialResult, IReadOnlyList<GridColumn<TItem>> columns, GridAggregates? totals, IReadOnlySet<string>? hiddenFields)
    {
        w.Write("<div data-netgrid-card");

        if (_options.VirtualScroll is { } virtualScroll)
        {
            // The card is the scroll viewport; the pager is replaced by scrolling. Browser scroll
            // anchoring is off: when blocks are swapped (and the focused row leaves the DOM) Chrome
            // would otherwise "keep its place" by jumping to the end of the spacers.
            w.Write(" data-virtual @scroll.passive=\"onVirtualScroll()\" style=\"overflow-anchor:none;height:");
            AppendEncoded(w, virtualScroll.Height);
            w.Write('"');
        }
        else if (_options.MinHeight.Length > 0)
        {
            w.Write(" style=\"min-height:");
            AppendEncoded(w, _options.MinHeight);
            w.Write('"');
        }

        w.Write(_options.VirtualScroll is null
            ? " class=\"relative overflow-x-auto rounded-2xl border border-neutral-200 bg-white shadow-sm dark:border-neutral-800 dark:bg-neutral-900\">"
            : " class=\"relative overflow-auto rounded-2xl border border-neutral-200 bg-white shadow-sm dark:border-neutral-800 dark:bg-neutral-900\">");
        // Visible focus outline for the keyboard-navigated body cells (roving tabindex in netopengrid.js).
        // Virtual scroll also keeps the header row on top, with an opaque background.
        w.Write(_options.VirtualScroll is null
            ? "<table class=\"min-w-full text-sm [&_td:focus-visible]:outline-2 [&_td:focus-visible]:-outline-offset-2 [&_td:focus-visible]:outline-brand-500 [&.is-resized_td]:overflow-hidden [&.is-resized_td]:text-ellipsis\">"
            : "<table class=\"min-w-full text-sm [&_td:focus-visible]:outline-2 [&_td:focus-visible]:-outline-offset-2 [&_td:focus-visible]:outline-brand-500 [&.is-resized_td]:overflow-hidden [&.is-resized_td]:text-ellipsis [&_thead_th]:sticky [&_thead_th]:top-0 [&_thead_th:not([data-pin]):not([data-pin-right])]:z-20 [&_thead_th]:bg-neutral-50 dark:[&_thead_th]:bg-[color:color-mix(in_oklab,var(--color-neutral-800)_60%,var(--color-neutral-900))]\">");

        AppendTableHead(w, columns, hiddenFields);

        w.Write("<tbody id=\"");
        AppendEncoded(w, _options.Id);
        w.Write("-body\" @keydown=\"onBodyKeydown($event)\" @focusin=\"onBodyFocusIn($event)\" @click=\"onRowClick($event)\" @auxclick=\"onRowClick($event)\">");

        AppendRows(w, initialResult.Page, BodyColumns(columns, hiddenFields), totals);

        w.Write("</tbody></table>");
        w.Write("</div>");
    }

    private void AppendTableHead(TextWriter w, IReadOnlyList<GridColumn<TItem>> columns, IReadOnlySet<string>? hiddenFields)
    {
        w.Write("<thead class=\"bg-neutral-50 dark:bg-neutral-800/60\" @keydown=\"onHeadKeydown($event)\"><tr>");

        if (_options.EnableRowSelection && _options.RowKey is not null)
        {
            // Pinned header cells need an opaque background: the thead's translucent one lets the
            // columns scrolling underneath show through. Same colour, mixed over the card.
            w.Write("<th scope=\"col\" data-pin=\"__select\" style=\"left:0\" class=\"sticky z-30 w-10 border-r border-neutral-200 bg-neutral-50 px-4 py-3 dark:border-neutral-800 dark:bg-[color:color-mix(in_oklab,var(--color-neutral-800)_60%,var(--color-neutral-900))]\">");
            w.Write("<input type=\"checkbox\" class=\"h-4 w-4 accent-brand-600\" aria-label=\"");
        w.Write(L("select.all.aria"));
        w.Write("\" :checked=\"allPageSelected\" @change=\"togglePageAll($event.target.checked)\">");
            w.Write("</th>");
        }

        var firstFilterableSeen = false;
        var hidesAny = BodyColumns(columns, hiddenFields).Count < columns.Count;

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

            if (hidesAny && hiddenFields!.Contains(column.Field))
            {
                // Left open on purpose: the next write closes the attribute value.
                w.Write("\" hidden=\"hidden");
            }

            if (column.IsPinned)
            {
                w.Write("\" data-pin=\"");
                AppendEncoded(w, column.Field);
                w.Write("\" style=\"left:0\" class=\"");
                w.Write(ResponsiveClass(column.HideBelow));
                w.Write("sticky z-30 relative border-r border-neutral-200 bg-neutral-50 dark:border-neutral-800 dark:bg-[color:color-mix(in_oklab,var(--color-neutral-800)_60%,var(--color-neutral-900))] ");
            }
            else if (column.IsPinnedRight)
            {
                w.Write("\" data-pin-right=\"");
                AppendEncoded(w, column.Field);
                w.Write("\" style=\"right:0\" class=\"");
                w.Write(ResponsiveClass(column.HideBelow));
                w.Write("sticky z-30 relative border-l border-neutral-200 bg-neutral-50 dark:border-neutral-800 dark:bg-[color:color-mix(in_oklab,var(--color-neutral-800)_60%,var(--color-neutral-900))] ");
            }
            else
            {
                w.Write("\" class=\"");
                w.Write(ResponsiveClass(column.HideBelow));
                w.Write("relative ");
            }

            w.Write("px-4 py-3 font-medium text-neutral-600 select-none dark:text-neutral-300 ");
            w.Write(TextAlignClass(column.Align));
            if (column.WidthCss is not null)
            {
                w.Write(' ');
                w.Write(column.WidthCss);
            }

            w.Write('"');
            if (column.IsSortable)
            {
                // Alpine keeps it in sync: primary sort ascending/descending, secondary sorts
                // "other", unsorted "none" (ARIA wants a single header marked as the sort).
                w.Write(" :aria-sort=\"ariaSort(");
                AppendJsQuoted(w, column.Field);
                w.Write(")\"");
            }

            w.Write("><div class=\"inline-flex items-center gap-1 ");
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
                w.Write("<span aria-hidden=\"true\" class=\"text-[10px] leading-none opacity-70\" x-show=\"sortDir(");
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
                w.Write(L("filter.aria").Replace("{field}", column.Label));
                w.Write("\">");
                w.Write(FunnelIcon);
                w.Write("</button>");
            }

            // Cycles unpinned -> left -> right -> unpinned; the label names the next step and the
            // icon mirrors while pinned right.
            w.Write("<button type=\"button\" class=\"cursor-pointer transition\" :class=\"pinButtonClass(");
            AppendJsQuoted(w, column.Field);
            w.Write(")\" @click=\"togglePin(");
            AppendJsQuoted(w, column.Field);
            w.Write(")\" :aria-label=\"pinLabel(");
            AppendJsQuoted(w, column.Field);
            w.Write(")\" aria-label=\"");
            w.Write(L("pin.left.aria").Replace("{field}", column.Label));
            w.Write("\">");
            w.Write(PinIcon);
            w.Write("</button>");

            w.Write("</div>");

            if (column.IsFilterable)
            {
                AppendFilterPopover(w, column, isFirstFilterable);
            }

            if (_options.EnableColumnResize)
            {
                // Mouse/touch affordance only (aria-hidden): keyboard users resize with Alt+Left/Right
                // on the header's buttons. @dragstart.prevent keeps the grab from starting a column move.
                w.Write("<span aria-hidden=\"true\" data-resize-handle class=\"absolute inset-y-0 right-0 z-10 w-1.5 cursor-col-resize touch-none select-none transition-colors hover:bg-brand-500/40\" draggable=\"false\" @dragstart.prevent @click.stop @pointerdown=\"startResize(");
                AppendJsQuoted(w, column.Field);
                w.Write(", $event)\" @dblclick=\"autoFitColumn(");
                AppendJsQuoted(w, column.Field);
                w.Write(")\"></span>");
            }

            w.Write("</th>");
        }

        if (_options.HasRowActions)
        {
            w.Write(_options.RowActionsPinned
                ? "<th scope=\"col\" data-actions data-pin-right=\"__actions\" style=\"right:0\" class=\"sticky z-30 border-l border-neutral-200 bg-neutral-50 px-4 py-3 text-right font-medium text-neutral-600 select-none dark:border-neutral-800 dark:bg-[color:color-mix(in_oklab,var(--color-neutral-800)_60%,var(--color-neutral-900))] dark:text-neutral-300\">"
                : "<th scope=\"col\" data-actions class=\"px-4 py-3 text-right font-medium text-neutral-600 select-none dark:text-neutral-300\">");
            AppendEncoded(w, _options.RowActionsHeader ?? L("actions.header"));
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
        w.Write(_options.VirtualScroll is null
            ? "<div class=\"flex items-center justify-between text-sm\">"
            : "<div class=\"flex items-center justify-between text-sm\" x-show=\"!isVirtual()\">");
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
        w.Write("<form method=\"post\" :action=\"");
        AppendJsQuoted(w, _assetOptions.RoutePrefix);
        w.Write(" + '/' + id + '/export'\" class=\"contents\">");
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

    private void AppendRows(TextWriter w, PageResult<TItem> page, IReadOnlyList<GridColumn<TItem>> columns, GridAggregates? totals)
    {
        if (page.Items.Count == 0)
        {
            w.Write("<tr><td colspan=\"");
            w.Write(ColumnSpan(columns).ToString(CultureInfo.InvariantCulture));

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

        // Virtual scroll stitches consecutive pages (blocks) together: the header totals only open the
        // first block, the footer totals only close the last one, and every row carries its absolute
        // index so the client can place blocks and keep keyboard focus across them.
        var isVirtual = _options.VirtualScroll is not null;
        if (!isVirtual || page.Page == 1)
        {
            AppendTotalsRow(w, GridAggregateRows.Header, totals, columns);
        }

        var index = (page.Page - 1) * page.PageSize;
        foreach (var item in page.Items)
        {
            AppendDataRow(w, item, columns, isVirtual ? index++ : null);
        }

        if (!isVirtual || page.Page >= page.TotalPages)
        {
            AppendTotalsRow(w, GridAggregateRows.Footer, totals, columns);
        }
    }

    private void AppendDataRow(TextWriter w, TItem item, IReadOnlyList<GridColumn<TItem>> columns, int? absoluteIndex = null)
    {
        var rowKey = _options.EnableRowSelection ? _options.RowKey?.Invoke(item) : null;

        var rowHref = _options.RowLink is { } rowLink ? GridUrl.Safe(rowLink(item)) : null;

        w.Write(rowHref is null
            ? "<tr class=\"border-b border-neutral-100 transition-colors last:border-0 hover:bg-brand-50/40 dark:border-neutral-800/60 dark:hover:bg-white/[0.04]\""
            : "<tr class=\"cursor-pointer border-b border-neutral-100 transition-colors last:border-0 hover:bg-brand-50/40 dark:border-neutral-800/60 dark:hover:bg-white/[0.04]\"");

        if (absoluteIndex is { } rowIndex)
        {
            w.Write(" data-row=\"");
            w.Write(rowIndex.ToString(CultureInfo.InvariantCulture));
            w.Write('"');
        }

        if (rowHref is not null)
        {
            w.Write(" data-href=\"");
            AppendEncoded(w, rowHref);
            w.Write('"');

            if (_options.RowLinkTarget is { } target)
            {
                w.Write(" data-target=\"");
                AppendEncoded(w, target);
                w.Write('"');
            }
        }

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
                w.Write("\" style=\"left:0\" class=\"");
                w.Write(ResponsiveClass(column.HideBelow));
                w.Write("sticky z-10 border-r border-neutral-100 bg-white hover:bg-brand-50/40 dark:border-neutral-800/60 dark:bg-neutral-900 dark:hover:bg-white/[0.04]");
            }
            else if (column.IsPinnedRight)
            {
                w.Write(" data-pin-right=\"");
                AppendEncoded(w, column.Field);
                w.Write("\" style=\"right:0\" class=\"");
                w.Write(ResponsiveClass(column.HideBelow));
                w.Write("sticky z-10 border-l border-neutral-100 bg-white hover:bg-brand-50/40 dark:border-neutral-800/60 dark:bg-neutral-900 dark:hover:bg-white/[0.04]");
            }
            else
            {
                w.Write(" class=\"");
                w.Write(ResponsiveClass(column.HideBelow));
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

        if (_options.HasRowActions)
        {
            AppendRowActions(w, item);
        }

        w.Write("</tr>");
    }

    /// <summary>
    /// Trailing actions cell. Links are plain anchors (unsafe schemes dropped); event buttons carry
    /// the row key and call rowAction(), which raises <c>netgrid:action</c> for the host page.
    /// </summary>
    private void AppendRowActions(TextWriter w, TItem item)
    {
        w.Write(_options.RowActionsPinned
            ? "<td data-actions data-pin-right=\"__actions\" style=\"right:0\" class=\"sticky z-10 whitespace-nowrap border-l border-neutral-100 bg-white px-4 py-2.5 text-right align-middle dark:border-neutral-800/60 dark:bg-neutral-900\"><div class=\"inline-flex items-center gap-3\">"
            : "<td data-actions class=\"whitespace-nowrap px-4 py-2.5 text-right align-middle\"><div class=\"inline-flex items-center gap-3\">");

        string? key = null;
        var keyResolved = false;

        foreach (var action in _options.RowActions)
        {
            if (action.Visible is { } visible && !visible(item))
            {
                continue;
            }

            var css = action.Style == RowActionStyle.Danger
                ? "cursor-pointer text-sm font-medium text-red-600 hover:underline dark:text-red-400"
                : "cursor-pointer text-sm font-medium text-brand-600 hover:underline dark:text-brand-400";

            if (action.Kind == RowActionKind.Link)
            {
                if (GridUrl.Safe(action.Href!(item)) is not { } href)
                {
                    continue;
                }

                w.Write("<a class=\"");
                w.Write(css);
                w.Write("\" href=\"");
                AppendEncoded(w, href);
                w.Write('"');
                if (action.Target is { } target)
                {
                    w.Write(" target=\"");
                    AppendEncoded(w, target);
                    w.Write('"');
                    if (target == "_blank")
                    {
                        w.Write(" rel=\"noopener\"");
                    }
                }

                w.Write('>');
                AppendEncoded(w, action.Label);
                w.Write("</a>");
                continue;
            }

            if (!keyResolved)
            {
                key = _options.RowKey?.Invoke(item);
                keyResolved = true;
            }

            if (key is null)
            {
                continue;
            }

            w.Write("<button type=\"button\" class=\"");
            w.Write(css);
            w.Write("\" data-key=\"");
            AppendEncoded(w, key);
            w.Write("\" @click=\"rowAction(");
            AppendJsQuoted(w, action.Name);
            w.Write(", $el.dataset.key)\">");
            AppendEncoded(w, action.Label);
            w.Write("</button>");
        }

        w.Write("</div></td>");
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

        w.Write("__NETGRID__.virtual=__NETGRID__.virtual||{};__NETGRID__.virtual[");
        AppendJsonString(w, _options.Id);
        w.Write("]=");
        w.Write(_options.VirtualScroll is { } virtualConfig
            ? "{\"blockSize\":" + virtualConfig.BlockSize.ToString(CultureInfo.InvariantCulture) + "}"
            : "null");
        w.Write(';');

        w.Write("__NETGRID__.views=__NETGRID__.views||{};__NETGRID__.views[");
        AppendJsonString(w, _options.Id);
        w.Write("]=[");
        for (var i = 0; i < _options.Views.Count; i++)
        {
            if (i > 0)
            {
                w.Write(',');
            }

            w.Write("{\"name\":");
            AppendJsonString(w, _options.Views[i].Name);
            w.Write(",\"query\":");
            AppendJsonString(w, _options.Views[i].Query);
            w.Write('}');
        }

        w.Write("];");

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
            AppendJsonString(w, _visibleColumns[i].Label);
            w.Write(",\"flipX\":\"");
            w.Write(IsFirstFilterable(_visibleColumns[i]) ? "left" : "right");
            w.Write("\",\"mode\":\"");
            w.Write(_visibleColumns[i].IsFilterable && IsListMode(_visibleColumns[i]) ? "list" : "op");
            w.Write("\",\"pin\":");
            w.Write(_visibleColumns[i].IsPinned ? "\"left\"" : _visibleColumns[i].IsPinnedRight ? "\"right\"" : "false");
            w.Write("}");
        }

        w.Write("];");
        w.Write("__NETGRID__.locale=");
        w.Write(JsonSerializer.Serialize(_locale.Strings, JsonOptions));
        w.Write(';');

        // Published so the client (netopengrid.js) builds its fetch/navigation URLs from the
        // server's actual data-endpoint prefix instead of a hardcoded "/netgrid" literal.
        w.Write("Object.assign(__NETGRID__,{\"prefix\":");
        AppendJsonString(w, _assetOptions.RoutePrefix);
        w.Write("});");

        w.Write("</script>");
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

    /// <summary>
    /// LITERAL class strings, one per breakpoint. Tailwind scans this file; a composed
    /// string (e.g. $"hidden {bp}:table-cell") would be invisible to the scan and the
    /// column would silently render unstyled.
    /// </summary>
    private static string ResponsiveClass(ResponsiveBreakpoint breakpoint) => breakpoint switch
    {
        ResponsiveBreakpoint.Sm => "hidden sm:table-cell ",
        ResponsiveBreakpoint.Md => "hidden md:table-cell ",
        ResponsiveBreakpoint.Lg => "hidden lg:table-cell ",
        ResponsiveBreakpoint.Xl => "hidden xl:table-cell ",
        _ => string.Empty,
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
