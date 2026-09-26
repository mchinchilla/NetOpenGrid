using NetOpenGrid.Domain;

namespace NetOpenGrid.Application.Options;

/// <summary>
/// A named grid state: the same query string the grid keeps in the URL (sort, filter, q, groupby,
/// expand, cols, hide, pageSize). Predefined views come from the server and every user sees them;
/// users can also save their own in the browser.
/// </summary>
public sealed record GridSavedView(string Name, string Query);

internal static class GridSavedViewValidator
{
    public const int MaxNameLength = 60;

    private static readonly HashSet<string> AllowedKeys =
        new(["sort", "filter", "q", "groupby", "expand", "cols", "hide", "pageSize"], StringComparer.Ordinal);

    /// <summary>
    /// Fails at configuration time on a view the grid could not honour: blank or duplicate names,
    /// unknown query keys, or fields that are not columns. The request parser would silently drop
    /// those, and a view that quietly shows something else is worse than a startup error.
    /// </summary>
    public static GridSavedView Validate(string name, string query, ISet<string> fields, ISet<string> seenNames)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            throw new GridConfigurationException($"View names need 1-{MaxNameLength} characters (got '{name}').");
        }

        name = name.Trim();
        if (!seenNames.Add(name))
        {
            throw new GridConfigurationException($"Duplicate view name '{name}'.");
        }

        var normalized = (query ?? string.Empty).Trim().TrimStart('?');

        foreach (var pair in normalized.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Unescape(eq < 0 ? pair : pair[..eq]);
            var value = eq < 0 ? string.Empty : Unescape(pair[(eq + 1)..]);

            if (!AllowedKeys.Contains(key))
            {
                throw new GridConfigurationException($"View '{name}': unknown parameter '{key}'.");
            }

            foreach (var field in FieldsOf(key, value))
            {
                if (!fields.Contains(field))
                {
                    throw new GridConfigurationException($"View '{name}': '{field}' in '{key}' is not a column.");
                }
            }
        }

        return new GridSavedView(name, normalized);
    }

    private static IEnumerable<string> FieldsOf(string key, string value) => key switch
    {
        "sort" => SplitList(value).Select(static t => t.LastIndexOf(':') is var i and >= 0 ? t[..i] : t),
        "filter" => SplitFilterTokens(value).Select(static t => t.Split(':')[0]),
        "groupby" or "cols" or "hide" => SplitList(value),
        _ => []
    };

    private static IEnumerable<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Commas inside an <c>in</c> filter's JSON array do not split tokens.</summary>
    private static IEnumerable<string> SplitFilterTokens(string value)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '[': depth++; break;
                case ']': depth--; break;
                case ',' when depth == 0:
                    if (i > start) yield return value[start..i].Trim();
                    start = i + 1;
                    break;
            }
        }

        if (start < value.Length) yield return value[start..].Trim();
    }

    private static string Unescape(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}
