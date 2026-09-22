using System.Linq.Expressions;
using System.Text.RegularExpressions;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Application.Strategies;

namespace NetOpenGrid.Application.Builders;

/// <summary>
/// Fluent builder for a single strongly-typed column. Selectors are captured as delegates
/// and strategies are compiled once at build time; the request path is reflection-free.
/// </summary>
public sealed class GridColumnBuilder<TSource, TKey>
{
    private readonly string _field;
    private Func<TSource, TKey> _selector;
    private Expression<Func<TSource, TKey>>? _selectorExpression;
    private string? _header;
    private string? _defaultHeader;
    private Func<TKey, string?>? _formatter;
    private IComparer<TKey>? _comparer;
    private GridValueParser<TKey>? _parser;
    private FilterOpSet _allowedOps = FilterOpSet.All;
    private bool? _sortable;
    private bool? _filterable;
    private bool? _searchable;
    private Func<TSource, string?, bool>? _searchMatcher;
    private Func<TSource, string?>? _rawCellHtml;
    private bool _visible = true;
    private bool _pinned;
    private ColumnAlign _align = ColumnAlign.Start;
    private string? _widthCss;
    private ColumnDataType? _dataType;

    internal GridColumnBuilder(string field, Func<TSource, TKey> selector) : this(field, selector, null)
    {
    }

    internal GridColumnBuilder(string field, Func<TSource, TKey> selector, Expression<Func<TSource, TKey>>? expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(selector);
        if (field.Contains(':'))
        {
            throw new GridConfigurationException($"Field '{field}' cannot contain ':' because it is used in request serialization.");
        }

        _field = field;
        _selector = selector;
        _selectorExpression = expression;
    }

    public string Field => _field;

    /// <summary>Sets the fallback header; explicit Header() still wins.</summary>
    internal void SetDefaultHeader(string header)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        // Kept apart from _header so that an explicit Header("") still has a readable name to
        // fall back on for menus and aria-labels.
        _defaultHeader ??= header;
        _header ??= header;
    }

    /// <summary>
    /// Sets the heading. An empty string is allowed and renders a blank <c>&lt;th&gt;</c>, for
    /// columns that only carry a row action or an icon; menus and aria-labels then fall back to
    /// the humanized field name, so nothing ends up nameless.
    /// </summary>
    public GridColumnBuilder<TSource, TKey> Header(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        _header = header;
        return this;
    }

    public GridColumnBuilder<TSource, TKey> Selector(Func<TSource, TKey> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = selector;
        return this;
    }

    /// <summary>Convenience overload; the expression is compiled once here (never per request).</summary>
    public GridColumnBuilder<TSource, TKey> Selector(Expression<Func<TSource, TKey>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        _selectorExpression = expression;
        _selector = expression.Compile();
        return this;
    }

    public GridColumnBuilder<TSource, TKey> Format(Func<TKey, string?> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _formatter = formatter;
        return this;
    }

    public GridColumnBuilder<TSource, TKey> Comparer(IComparer<TKey> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        _comparer = comparer;
        return this;
    }

    /// <summary>Custom value parser for filters. Defaults are built-in for common scalar types.</summary>
    public GridColumnBuilder<TSource, TKey> Parser(GridValueParser<TKey> parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
        return this;
    }

    public GridColumnBuilder<TSource, TKey> AllowedOps(FilterOpSet ops)
    {
        _allowedOps = ops;
        return this;
    }

    public GridColumnBuilder<TSource, TKey> Sortable(bool sortable = true)
    {
        _sortable = sortable;
        return this;
    }

    public GridColumnBuilder<TSource, TKey> Filterable(bool filterable = true)
    {
        _filterable = filterable;
        return this;
    }

    public GridColumnBuilder<TSource, TKey> Searchable(bool searchable = true)
    {
        _searchable = searchable;
        return this;
    }

    /// <summary>Custom global-search matcher; enables search for non-text keys too.</summary>
    public GridColumnBuilder<TSource, TKey> SearchMatch(Func<TSource, string?, bool> matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        _searchMatcher = matcher;
        return this;
    }

    /// <summary>Trusted raw HTML cell renderer (server-controlled only).</summary>
    public GridColumnBuilder<TSource, TKey> RawCellHtml(Func<TSource, string?> renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _rawCellHtml = renderer;
        return this;
    }

    public GridColumnBuilder<TSource, TKey> Visible(bool visible = true)
    {
        _visible = visible;
        return this;
    }

    /// <summary>Pins the column to the left edge (stays visible during horizontal scroll).</summary>
    public GridColumnBuilder<TSource, TKey> Pinned(bool pinned = true)
    {
        _pinned = pinned;
        return this;
    }

    public GridColumnBuilder<TSource, TKey> Align(ColumnAlign align)
    {
        _align = align;
        return this;
    }

    public GridColumnBuilder<TSource, TKey> WidthCss(string css)
    {
        _widthCss = css;
        return this;
    }

    public GridColumnBuilder<TSource, TKey> DataType(ColumnDataType dataType)
    {
        _dataType = dataType;
        return this;
    }

    internal GridColumn<TSource> Build()
    {
        var dataType = _dataType ?? InferDataType();
        var sortable = _sortable ?? true;
        var filterable = _filterable ?? true;

        ISortStrategy<TSource>? sortStrategy = null;
        if (sortable)
        {
            sortStrategy = new KeySortStrategy<TSource, TKey>(_selector, ResolveComparer());
        }

        IFilterStrategyFactory<TSource>? filterFactory = null;
        var allowedOps = FilterOpSet.None;
        if (filterable)
        {
            var preset = FilterPresets.For(dataType);
            var requested = _allowedOps & preset;
            (filterFactory, allowedOps) = ResolveFilterFactory(requested);
            if (allowedOps == FilterOpSet.None)
            {
                filterFactory = null;
            }
        }

        var searchStrategy = ResolveSearch();

        return new GridColumn<TSource>
        {
            Field = _field,
            Header = _header ?? HumanizeFieldName(_field),
            Label = !string.IsNullOrWhiteSpace(_header) ? _header : _defaultHeader ?? HumanizeFieldName(_field),
            Format = ResolveFormatter(),
            RawCellHtml = _rawCellHtml,
            SortStrategy = sortStrategy,
            FilterFactory = filterFactory,
            SearchStrategy = searchStrategy,
            SelectorExpression = _selectorExpression,
            FilterValueParser = BuildFilterValueParser(),
            RawKeyFormat = BuildRawKeyFormat(),
            KeyFormatter = BuildKeyFormatter(),
            AllowedOps = allowedOps,
            IsSortable = sortStrategy is not null,
            IsSearchable = searchStrategy is not null,
            IsVisible = _visible,
            IsPinned = _pinned,
            DataType = dataType,
            Align = _align,
            WidthCss = _widthCss
        };
    }

    private IComparer<TKey> ResolveComparer() =>
        _comparer ??
        (typeof(TKey) == typeof(string)
            ? (IComparer<TKey>)(object)StringComparer.Ordinal
            : Comparer<TKey>.Default);

    /// <summary>
    /// Non-generic literal parser for SQL push-down sources: resolved once per column
    /// (TKey known at build time) from the custom parser or the built-in tables.
    /// </summary>
    private GridFilterValueParser? BuildFilterValueParser()
    {
        if (_parser is not null)
        {
            return Wrap(_parser);
        }

        if (typeof(TKey) == typeof(string))
        {
            return static (string raw, out object? parsed) =>
            {
                parsed = raw;
                return true;
            };
        }

        var scalar = DefaultValueParsers<TKey>.TryGet();
        if (scalar is not null)
        {
            return Wrap(scalar);
        }

        var enumParser = DefaultValueParsers<TKey>.TryGetEnum();
        if (enumParser is not null)
        {
            return Wrap(enumParser);
        }

        if (typeof(TKey) == typeof(bool))
        {
            return static (string raw, out object? parsed) =>
            {
                var ok = bool.TryParse(raw, out var value);
                parsed = value;
                return ok;
            };
        }

        return null;
    }

    private static GridFilterValueParser Wrap(GridValueParser<TKey> parser) =>
        (string raw, out object? parsed) =>
        {
            var ok = parser(raw, out var typed);
            parsed = typed;
            return ok;
        };

    /// <summary>Raw, parseable-back key display (ignores custom cell formatting on purpose).</summary>
    private Func<TSource, string?> BuildRawKeyFormat()
    {
        var selector = _selector;
        return item => DefaultValueFormatter<TKey>.Format(selector(item));
    }

    private Func<object?, string?>? BuildKeyFormatter()
    {
        var formatter = _formatter;
        return formatter is not null
            ? boxed => boxed is TKey key ? formatter(key) : null
            : static boxed => boxed is TKey key ? DefaultValueFormatter<TKey>.Format(key) : null;
    }

    private Func<TSource, string?> ResolveFormatter()
    {
        var formatter = _formatter;
        var selector = _selector;

        return formatter is not null
            ? item => formatter(selector(item))
            : item => DefaultValueFormatter<TKey>.Format(selector(item));
    }

    private (IFilterStrategyFactory<TSource>? Factory, FilterOpSet Ops) ResolveFilterFactory(FilterOpSet requested)
    {
        if (requested == FilterOpSet.None)
        {
            return (null, FilterOpSet.None);
        }

        if (typeof(TKey) == typeof(string))
        {
            var ops = requested & (FilterOpSet.Text | FilterOpSet.Numeric);
            var stringSelector = (Func<TSource, string?>)(object)_selector;
            return ops == FilterOpSet.None
                ? (null, FilterOpSet.None)
                : (new StringFilterStrategy<TSource>(stringSelector), ops);
        }

        var parsable = ParsableFilterStrategy<TSource, TKey>.TryCreateDefault(_selector, _parser);
        if (parsable is not null)
        {
            var ops = requested & parsable.SupportedOps;
            return ops == FilterOpSet.None
                ? (null, FilterOpSet.None)
                : (parsable, ops);
        }

        if (typeof(TKey).IsEnum)
        {
            return ResolveEnumFactory(requested);
        }

        if (typeof(TKey) == typeof(bool))
        {
            var boolOps = requested & (FilterOpSet.Equals | FilterOpSet.NotEquals | FilterOpSet.In);
            if (boolOps == FilterOpSet.None)
            {
                return (null, FilterOpSet.None);
            }

            var boolSelector = (Func<TSource, bool>)(object)_selector;
            return (new BoolFilterStrategy<TSource>(boolSelector), boolOps);
        }

        if (typeof(TKey).IsValueType)
        {
            return (null, FilterOpSet.None);
        }

        var equality = new EqualityFilterStrategy<TSource, TKey>(_selector);
        var eqOps = requested & equality.SupportedOps;
        return eqOps == FilterOpSet.None
            ? (null, FilterOpSet.None)
            : (equality, eqOps);
    }

    private (IFilterStrategyFactory<TSource>? Factory, FilterOpSet Ops) ResolveEnumFactory(FilterOpSet requested)
    {
        var parser = DefaultValueParsers<TKey>.TryGetEnum();
        if (parser is null)
        {
            return (null, FilterOpSet.None);
        }

        var ops = requested & (FilterOpSet.Equals | FilterOpSet.NotEquals | FilterOpSet.In);
        return ops == FilterOpSet.None
            ? (null, FilterOpSet.None)
            : (new ParsableFilterStrategy<TSource, TKey>(_selector, parser), ops);
    }

    private ISearchStrategy<TSource>? ResolveSearch()
    {
        if (_searchMatcher is not null)
        {
            return new CustomSearchStrategy<TSource>(_searchMatcher);
        }

        if (_searchable.HasValue && !_searchable.Value)
        {
            return null;
        }

        if (_searchable == true || typeof(TKey) == typeof(string))
        {
            return CreateDefaultSearch();
        }

        return null;
    }

    private ISearchStrategy<TSource>? CreateDefaultSearch()
    {
        if (typeof(TKey) == typeof(string))
        {
            var stringSelector = (Func<TSource, string?>)(object)_selector;
            return new StringSearchStrategy<TSource>(stringSelector);
        }

        return null;
    }

    private ColumnDataType InferDataType()
    {
        if (typeof(TKey) == typeof(string))
        {
            return ColumnDataType.Text;
        }

        if (typeof(TKey).IsEnum)
        {
            return ColumnDataType.Enum;
        }

        return typeof(TKey) switch
        {
            Type t when t == typeof(bool) => ColumnDataType.Boolean,
            Type t when t == typeof(DateOnly) || t == typeof(DateTime) || t == typeof(DateTimeOffset) => ColumnDataType.Date,
            Type t when t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort) ||
                        t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) ||
                        t == typeof(float) || t == typeof(double) || t == typeof(decimal) => ColumnDataType.Numeric,
            _ => ColumnDataType.Unknown
        };
    }

    internal static string HumanizeFieldName(string field) => ColumnNameHumanizer.Humanize(field);
}

internal static class FilterPresets
{
    public static FilterOpSet For(ColumnDataType dataType) => dataType switch
    {
        ColumnDataType.Text => FilterOpSet.Text,
        ColumnDataType.Numeric or ColumnDataType.Date => FilterOpSet.Numeric,
        ColumnDataType.Boolean or ColumnDataType.Enum or ColumnDataType.Unknown => FilterOpSet.EqualityOnly,
        _ => FilterOpSet.EqualityOnly
    };
}
