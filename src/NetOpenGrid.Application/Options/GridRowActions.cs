using NetOpenGrid.Domain;

namespace NetOpenGrid.Application.Options;

public enum RowActionKind
{
    /// <summary>A plain link (<c>&lt;a href&gt;</c>).</summary>
    Link,

    /// <summary>A button that raises the <c>netgrid:action</c> DOM event for the host page to handle.</summary>
    Event
}

public enum RowActionStyle
{
    Default,
    Danger
}

/// <summary>One entry of the row actions column. Built once; the delegates run per rendered row.</summary>
public sealed record GridRowAction<T>(
    RowActionKind Kind,
    string Name,
    string Label,
    Func<T, string?>? Href,
    string? Target,
    RowActionStyle Style,
    Func<T, bool>? Visible);

/// <summary>Fluent list of row actions (<c>WithRowActions(a =&gt; a.Link(...).Event(...))</c>).</summary>
public sealed class GridRowActionsBuilder<T>
{
    private readonly List<GridRowAction<T>> _actions = [];
    private readonly HashSet<string> _eventNames = new(StringComparer.Ordinal);

    public string? HeaderText { get; private set; }

    public bool IsPinned { get; private set; }

    /// <summary>Pins the actions column to the right edge (it stays in view while scrolling sideways).</summary>
    public GridRowActionsBuilder<T> Pinned(bool pinned = true)
    {
        IsPinned = pinned;
        return this;
    }

    /// <summary>A link per row. <paramref name="href"/> returning null (or an unsafe scheme) hides it for that row.</summary>
    public GridRowActionsBuilder<T> Link(
        string label,
        Func<T, string?> href,
        string? target = null,
        RowActionStyle style = RowActionStyle.Default,
        Func<T, bool>? visible = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(href);
        _actions.Add(new GridRowAction<T>(RowActionKind.Link, label, label, href, target, style, visible));
        return this;
    }

    /// <summary>
    /// A button that raises <c>netgrid:action</c> with <c>{ grid, action: name, key }</c> on the grid root
    /// (and posts the same payload to a same-origin parent when the grid lives in an iframe).
    /// Needs a row key: <c>WithRowKey(...)</c> or <c>EnableRowSelection(...)</c>.
    /// </summary>
    public GridRowActionsBuilder<T> Event(
        string name,
        string label,
        RowActionStyle style = RowActionStyle.Default,
        Func<T, bool>? visible = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!_eventNames.Add(name))
        {
            throw new GridConfigurationException($"Duplicate row action event '{name}'.");
        }

        _actions.Add(new GridRowAction<T>(RowActionKind.Event, name, label, null, null, style, visible));
        return this;
    }

    /// <summary>Header text of the actions column (defaults to the localized "Actions").</summary>
    public GridRowActionsBuilder<T> Header(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        HeaderText = header;
        return this;
    }

    internal IReadOnlyList<GridRowAction<T>> Build() => _actions.ToArray();
}

/// <summary>Only relative, http(s) and mailto/tel URLs are rendered; anything else (javascript:, data:, ...) is dropped.</summary>
public static class GridUrl
{
    public static string? Safe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();
        var colon = trimmed.IndexOf(':');
        var slash = trimmed.IndexOfAny(['/', '?', '#']);

        // No scheme (relative URL), or the colon only appears after the path started.
        if (colon < 0 || (slash >= 0 && slash < colon))
        {
            return trimmed;
        }

        var scheme = trimmed[..colon].ToLowerInvariant();
        return scheme is "http" or "https" or "mailto" or "tel" ? trimmed : null;
    }
}
