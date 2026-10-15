using System.Text.RegularExpressions;
using NetOpenGrid.Domain;

namespace NetOpenGrid.Application.Options;

/// <summary>
/// The brand colour a host hands to the grid (<c>NetOpenGridAssetOptions.BrandColor</c>,
/// <c>WithBrandColor(...)</c>). It ends up verbatim inside a <c>&lt;style&gt;</c> element of the
/// grid document (<c>:root{--ng-brand:…}</c>), where HTML encoding does not apply, so only plain
/// colour syntax is accepted: <c>#rgb</c>/<c>#rgba</c>/<c>#rrggbb</c>/<c>#rrggbbaa</c>, a named colour
/// (<c>teal</c>) or one colour function without nesting — <c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c>,
/// <c>hsla()</c>, <c>hwb()</c>, <c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c>, <c>color()</c>.
/// Anything that could close the declaration or the element (<c>;</c> <c>{</c> <c>}</c> <c>&lt;</c>,
/// quotes, backslashes, <c>url(</c>, nested functions) is rejected.
/// </summary>
public static class GridCssColor
{
    private const int MaxLength = 128;

    private static readonly Regex HexColor = new(
        "^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NamedColor = new(
        "^[a-zA-Z]{3,32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Arguments: numbers, units/keywords (deg, none, from, display-p3...), '%', ',', '/', '.', '+', '-'
    // and spaces. No parentheses inside, so no nested calc()/var()/url().
    private static readonly Regex FunctionColor = new(
        @"^(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch|color)\([-+.,/% a-zA-Z0-9]*\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // CSS-wide keywords are syntactically "named" but mean nothing as a brand colour.
    private static readonly HashSet<string> CssWideKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "inherit", "initial", "unset", "revert"
    };

    /// <summary>True when <paramref name="value"/> (trimmed) is a colour this policy accepts.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var color = value.Trim();
        if (color.Length > MaxLength)
        {
            return false;
        }

        return HexColor.IsMatch(color)
            || FunctionColor.IsMatch(color)
            || (NamedColor.IsMatch(color) && !CssWideKeywords.Contains(color));
    }

    /// <summary>Returns the trimmed colour, or throws <see cref="GridConfigurationException"/> if it is not accepted.</summary>
    public static string Validate(string? value)
    {
        if (!IsValid(value))
        {
            throw new GridConfigurationException(
                $"'{value}' is not an accepted brand colour. Use #rrggbb, a named colour or a single " +
                "rgb()/hsl()/hwb()/lab()/lch()/oklab()/oklch()/color() function.");
        }

        return value!.Trim();
    }
}
