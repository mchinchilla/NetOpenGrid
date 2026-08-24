using System.Text.Json;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Binding;

public sealed record GridQueryNormalization(GridQuery Query, IReadOnlyList<string> Warnings);

/// <summary>
/// Parses and normalizes raw request values into a safe <see cref="GridQuery"/>.
/// Unknown fields/operators are dropped (never trusted) with a warning; paging is clamped.
/// </summary>
public static class GridRequestParser
{
    private const string PageParam = "page";
    private const string PageSizeParam = "pageSize";
    private const string SortParam = "sort";
    private const string FilterParam = "filter";
    private const string SearchParam = "q";
    private const int MaxSearchLength = 200;

    public static GridQueryNormalization Parse<T>(GridRequestValues values, GridOptions<T> options)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();

        var page = Math.Max(ParseIntOr(values.Get(PageParam), 1), PageRequest.FirstPage);
        var requestedPageSize = ParseIntOr(values.Get(PageSizeParam), options.DefaultPageSize);
        var pageSize = Math.Clamp(requestedPageSize, 1, options.MaxPageSize);

        if (requestedPageSize > options.MaxPageSize)
        {
            warnings.Add($"pageSize '{requestedPageSize}' clamped to {options.MaxPageSize}.");
        }

        var sorts = ParseSorts(values.GetAll(SortParam), options, warnings);
        var filters = ParseFilters(values.GetAll(FilterParam), options, warnings);
        var search = ParseSearch(values.Get(SearchParam), warnings);

        var query = new GridQuery(
            new PageRequest(page, pageSize),
            sorts,
            filters,
            search);

        return new GridQueryNormalization(query, warnings);
    }

    private static List<SortDescriptor> ParseSorts<T>(
        IReadOnlyList<string> rawValues,
        GridOptions<T> options,
        List<string> warnings)
    {
        var sorts = new List<SortDescriptor>();

        foreach (var raw in rawValues)
        {
            foreach (var token in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = token.LastIndexOf(':');
                var field = separatorIndex < 0 ? token : token[..separatorIndex];
                var directionToken = separatorIndex < 0 ? null : token[(separatorIndex + 1)..];

                SortDirection direction;
                if (string.IsNullOrEmpty(directionToken) || directionToken.Equals("asc", StringComparison.OrdinalIgnoreCase))
                {
                    direction = SortDirection.Ascending;
                }
                else if (directionToken!.Equals("desc", StringComparison.OrdinalIgnoreCase))
                {
                    direction = SortDirection.Descending;
                }
                else
                {
                    warnings.Add($"Ignored sort direction '{directionToken}' for field '{field}'.");
                    direction = SortDirection.Ascending;
                }

                if (!options.TryGetColumn(field, out var column))
                {
                    warnings.Add($"Ignored unknown sort field '{field}'.");
                    continue;
                }

                if (!column.IsSortable)
                {
                    warnings.Add($"Ignored sort on non-sortable field '{field}'.");
                    continue;
                }

                sorts.Add(new SortDescriptor(column.Field, direction));
            }
        }

        return sorts;
    }

    private static List<FilterDescriptor> ParseFilters<T>(
        IReadOnlyList<string> rawValues,
        GridOptions<T> options,
        List<string> warnings)
    {
        var filters = new List<FilterDescriptor>();

        foreach (var raw in rawValues)
        {
            foreach (var token in SplitFilterTokens(raw))
            {
                var parts = token.Split(':', 3);
                if (parts.Length is not (2 or 3))
                {
                    warnings.Add($"Ignored malformed filter '{token}'.");
                    continue;
                }

                var field = parts[0];

                // Compact form "field:>=100" (operator symbol glued to the value).
                if (parts.Length == 2 && FilterOperatorMapper.TryParseLeadingSymbol(parts[1], out var symbolOp, out var symbolValue))
                {
                    parts = [field, FilterOperatorMapper.ToToken(symbolOp), symbolValue];
                }

                if (!FilterOperatorMapper.TryParse(parts[1], out var op))
                {
                    warnings.Add($"Ignored filter operator '{parts[1]}' for field '{field}'.");
                    continue;
                }

                var value = parts.Length == 3 ? parts[2] : string.Empty;
                var isEmptyOp = op is FilterOperator.IsEmpty or FilterOperator.IsNotEmpty;

                if (parts.Length == 2 && !isEmptyOp)
                {
                    warnings.Add($"Ignored filter '{token}': missing value.");
                    continue;
                }

                if (!options.TryGetColumn(field, out var column))
                {
                    warnings.Add($"Ignored unknown filter field '{field}'.");
                    continue;
                }

                if (!column.IsFilterable || !column.AllowedOps.HasFlag(FilterOperatorMapper.ToSet(op)))
                {
                    warnings.Add($"Ignored disallowed operator '{op}' for field '{field}'.");
                    continue;
                }

                if (op == FilterOperator.In && !IsValidInPayload(value))
                {
                    warnings.Add($"Ignored filter '{token}': 'in' requires a non-empty JSON string array.");
                    continue;
                }

                filters.Add(new FilterDescriptor(column.Field, op, isEmptyOp ? null : value));
            }
        }

        return filters;
    }

    private static bool IsValidInPayload(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) is { Count: > 0 };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Comma-splits filter tokens, but commas inside [ ... ] (the In operator's JSON payload) stay intact.
    /// </summary>
    private static IEnumerable<string> SplitFilterTokens(string raw)
    {
        if (!raw.Contains('['))
        {
            return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        var tokens = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < raw.Length; i++)
        {
            switch (raw[i])
            {
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    tokens.Add(raw[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        tokens.Add(raw[start..].Trim());
        return tokens.Where(static t => t.Length > 0);
    }

    private static string? ParseSearch(string? raw, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxSearchLength)
        {
            warnings.Add($"Search term truncated to {MaxSearchLength} characters.");
            trimmed = trimmed[..MaxSearchLength];
        }

        return trimmed;
    }

    private static int ParseIntOr(string? raw, int fallback) =>
        int.TryParse(raw, out var parsed) ? parsed : fallback;
}
