using System.Text.RegularExpressions;
using NetOpenGrid.Domain;

namespace NetOpenGrid.Application.Options;

/// <summary>How long a row stays selected once the user ticks it.</summary>
public enum GridSelectionScope
{
    /// <summary>
    /// The selection survives paging, sorting, filtering and searching, so it can hold keys of rows
    /// that are not on screen. The default.
    /// </summary>
    All,

    /// <summary>
    /// The selection only covers the rows on screen: changing page, page size, sort, filters, search
    /// or grouping empties it, so an event never carries keys the user cannot see.
    /// </summary>
    Page
}

/// <summary>Colour of a bulk action button in the selection bar.</summary>
public enum BulkActionTone
{
    Primary,
    Success,
    Danger,
    Neutral
}

/// <summary>
/// One button of the selection bar. Clicking it raises <c>netgrid:bulk-action</c> with
/// <c>{ grid, action: Name, keys, count }</c>; the library runs nothing on the server.
/// Built once; <see cref="When"/> runs per rendered row.
/// </summary>
public sealed record GridBulkAction<T>(
    string Name,
    string Label,
    string? Icon,
    BulkActionTone Tone,
    bool Confirm,
    Func<T, bool>? When);

/// <summary>Fluent configuration of one bulk action (<c>AddBulkAction("post", a =&gt; a.Label(...))</c>).</summary>
public sealed class GridBulkActionBuilder<T>
{
    private string? _label;
    private string? _icon;
    private BulkActionTone _tone = BulkActionTone.Primary;
    private bool _confirm;
    private Func<T, bool>? _when;

    /// <summary>Button text (goes through <c>TextLocalizer</c>). Defaults to the action name.</summary>
    public GridBulkActionBuilder<T> Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _label = label;
        return this;
    }

    /// <summary>
    /// CSS class of an icon rendered before the label as <c>&lt;i class="..."&gt;</c>, emitted as written
    /// (e.g. <c>"fas fa-check-double"</c>). The icon font is the host's: link it with <c>WithStylesheet</c>.
    /// </summary>
    public GridBulkActionBuilder<T> Icon(string? cssClass)
    {
        _icon = string.IsNullOrWhiteSpace(cssClass) ? null : cssClass.Trim();
        return this;
    }

    public GridBulkActionBuilder<T> Tone(BulkActionTone tone)
    {
        _tone = tone;
        return this;
    }

    /// <summary>
    /// True makes the grid ask with the browser's own confirm dialog before raising the event.
    /// Off by default: the host page normally confirms with its own dialog.
    /// </summary>
    public GridBulkActionBuilder<T> Confirm(bool confirm = true)
    {
        _confirm = confirm;
        return this;
    }

    /// <summary>
    /// Rows this action applies to. The button is enabled only while every selected row admits the
    /// action (each row carries the list in <c>data-bulk</c>). Without it the action applies to any row.
    /// </summary>
    public GridBulkActionBuilder<T> When(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _when = predicate;
        return this;
    }

    internal GridBulkAction<T> Build(string name) => new(name, _label ?? name, _icon, _tone, _confirm, _when);
}

internal static partial class GridBulkActionRules
{
    // The name travels in a space-separated data-bulk attribute and in a JS string literal.
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,63}$")]
    private static partial Regex NamePattern();

    public static GridBulkAction<T> Create<T>(
        string name,
        Action<GridBulkActionBuilder<T>>? configure,
        IReadOnlyList<GridBulkAction<T>> existing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!NamePattern().IsMatch(name))
        {
            throw new GridConfigurationException(
                $"Bulk action name '{name}' is invalid. Use 1-64 chars: letters, digits, '-' or '_', starting with a letter.");
        }

        if (existing.Any(a => string.Equals(a.Name, name, StringComparison.Ordinal)))
        {
            throw new GridConfigurationException($"Duplicate bulk action '{name}'.");
        }

        var builder = new GridBulkActionBuilder<T>();
        configure?.Invoke(builder);
        return builder.Build(name);
    }

    public static void Validate(bool enableRowSelection, bool hasBulkActions, bool hasRowSelectable)
    {
        if (hasBulkActions && !enableRowSelection)
        {
            throw new GridConfigurationException("Bulk actions need row selection: call EnableRowSelection(...).");
        }

        if (hasRowSelectable && !enableRowSelection)
        {
            throw new GridConfigurationException("RowSelectable needs row selection: call EnableRowSelection(...).");
        }
    }
}
