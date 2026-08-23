using System.Linq.Expressions;
using System.Text.RegularExpressions;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Builders;

/// <summary>
/// Fluent builder producing an immutable <see cref="GridOptions{T}"/>.
/// Validation happens once at Build(); the request path never re-validates.
/// </summary>
public sealed class GridOptionsBuilder<T>
{
    private readonly List<GridColumn<T>> _columns = [];
    private readonly HashSet<string> _fields = new(StringComparer.Ordinal);
    private string? _id;
    private string _title = "Data grid";
    private string _subtitle = string.Empty;
    private int _defaultPageSize = PageRequest.DefaultPageSize;
    private int _maxPageSize = 500;
    private int[] _pageSizeChoices = [10, 25, 50, 100];
    private int _debounceMilliseconds = 300;
    private string _theme = "grid";
    private string _minHeight = "64rem";
    private string _emptyMessage = "No records found.";
    private Func<T, string?>? _rowKey;
    private bool _enableRowSelection;
    private List<GridNavLink> _navLinks = [];

    public GridOptionsBuilder<T> WithId(string id)
    {
        _id = id;
        return this;
    }

    public GridOptionsBuilder<T> WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public GridOptionsBuilder<T> WithSubtitle(string subtitle)
    {
        _subtitle = subtitle;
        return this;
    }

    public GridOptionsBuilder<T> WithDefaultPageSize(int pageSize)
    {
        _defaultPageSize = pageSize;
        return this;
    }

    public GridOptionsBuilder<T> WithMaxPageSize(int maxPageSize)
    {
        _maxPageSize = maxPageSize;
        return this;
    }

    public GridOptionsBuilder<T> WithPageSizeChoices(params int[] choices)
    {
        _pageSizeChoices = choices;
        return this;
    }

    public GridOptionsBuilder<T> WithDebounce(int milliseconds)
    {
        _debounceMilliseconds = milliseconds;
        return this;
    }

    public GridOptionsBuilder<T> WithTheme(string themeName)
    {
        _theme = themeName;
        return this;
    }

    /// <summary>Minimum table-card height (CSS value, default ≈25 rows). Pass "" to disable.</summary>
    public GridOptionsBuilder<T> WithMinHeight(string minHeight)
    {
        _minHeight = minHeight?.Trim() ?? string.Empty;
        return this;
    }

    public GridOptionsBuilder<T> WithEmptyMessage(string message)
    {
        _emptyMessage = message;
        return this;
    }

    /// <summary>Enables client-side row selection. Keys must be stable and unique.</summary>
    public GridOptionsBuilder<T> EnableRowSelection(Func<T, string?> rowKey)
    {
        ArgumentNullException.ThrowIfNull(rowKey);
        _enableRowSelection = true;
        _rowKey = rowKey;
        return this;
    }

    public GridOptionsBuilder<T> WithNavLinks(params GridNavLink[] links)
    {
        _navLinks = [.. links];
        return this;
    }

    public GridOptionsBuilder<T> AddColumn<TKey>(
        string field,
        Func<T, TKey> selector,
        Action<GridColumnBuilder<T, TKey>>? configure = null)
    {
        var columnBuilder = new GridColumnBuilder<T, TKey>(field, selector);
        configure?.Invoke(columnBuilder);

        if (!_fields.Add(columnBuilder.Field))
        {
            throw new GridConfigurationException($"Duplicate grid column field '{columnBuilder.Field}'.");
        }

        _columns.Add(columnBuilder.Build());
        return this;
    }

    /// <summary>Adds a column deriving the field name from the member expression (startup-time sugar).</summary>
    public GridOptionsBuilder<T> AddColumn<TKey>(
        Expression<Func<T, TKey>> selector,
        Action<GridColumnBuilder<T, TKey>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var rawName = ExtractMemberName(selector);
        var columnBuilder = new GridColumnBuilder<T, TKey>(Camelize(rawName), selector.Compile());
        columnBuilder.SetDefaultHeader(GridColumnBuilder<T, TKey>.HumanizeFieldName(rawName));
        configure?.Invoke(columnBuilder);

        if (!_fields.Add(columnBuilder.Field))
        {
            throw new GridConfigurationException($"Duplicate grid column field '{columnBuilder.Field}'.");
        }

        _columns.Add(columnBuilder.Build());
        return this;
    }

    public GridOptions<T> Build()
    {
        ValidateId();
        ValidatePaging();
        ValidateColumns();

        return new GridOptions<T>
        {
            Id = _id!,
            Title = _title,
            Subtitle = _subtitle,
            Columns = _columns.ToArray(),
            DefaultPageSize = _defaultPageSize,
            MaxPageSize = _maxPageSize,
            PageSizeChoices = _pageSizeChoices,
            DebounceMilliseconds = _debounceMilliseconds,
            Theme = _theme,
            MinHeight = _minHeight,
            EmptyMessage = _emptyMessage,
            EnableRowSelection = _enableRowSelection,
            RowKey = _rowKey,
            NavLinks = _navLinks.ToArray()
        };
    }

    private void ValidateId()
    {
        if (string.IsNullOrWhiteSpace(_id))
        {
            throw new GridConfigurationException("Grid id is required. Call WithId().");
        }

        if (!IdPattern.IsMatch(_id))
        {
            throw new GridConfigurationException(
                $"Grid id '{_id}' is invalid. Use 1-64 chars: letters, digits, '-' or '_'.");
        }
    }

    private void ValidatePaging()
    {
        if (_maxPageSize < 1)
        {
            throw new GridConfigurationException("MaxPageSize must be >= 1.");
        }

        if (_defaultPageSize is < 1 || _defaultPageSize > _maxPageSize)
        {
            throw new GridConfigurationException(
                $"DefaultPageSize ({_defaultPageSize}) must be between 1 and MaxPageSize ({_maxPageSize}).");
        }

        if (_pageSizeChoices.Length == 0)
        {
            throw new GridConfigurationException("At least one page size choice is required.");
        }

        if (_pageSizeChoices.Any(c => c is < 1 || c > _maxPageSize))
        {
            throw new GridConfigurationException($"All page size choices must be between 1 and MaxPageSize ({_maxPageSize}).");
        }

        _pageSizeChoices = _pageSizeChoices.Distinct().Order().ToArray();
    }

    private void ValidateColumns()
    {
        if (_columns.Count == 0)
        {
            throw new GridConfigurationException("At least one column is required.");
        }

        if (_columns.All(static c => !c.IsVisible))
        {
            throw new GridConfigurationException("At least one visible column is required.");
        }

        if (_enableRowSelection && _rowKey is null)
        {
            throw new GridConfigurationException("EnableRowSelection requires a RowKey function.");
        }
    }

    private static string ExtractMemberName<TKey>(Expression<Func<T, TKey>> selector)
    {
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? unary.Operand
            : selector.Body;

        return body is MemberExpression member
            ? member.Member.Name
            : throw new GridConfigurationException(
                "Expression selectors must be simple member access (e.g. e => e.Name). " +
                "Use the (field, selector) overload for computed columns.");
    }

    private static string Camelize(string name) =>
        name.Length > 0 && char.IsUpper(name[0])
            ? char.ToLowerInvariant(name[0]) + name[1..]
            : name;

    private static readonly Regex IdPattern = new("^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.Compiled);
}
