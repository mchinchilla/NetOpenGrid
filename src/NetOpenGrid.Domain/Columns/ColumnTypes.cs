namespace NetOpenGrid.Domain.Columns;

public enum ColumnDataType
{
    Text,
    Numeric,
    Date,
    Boolean,
    Enum,
    Unknown
}

public enum ColumnAlign
{
    Start,
    Center,
    End
}

/// <summary>
/// Breakpoint below which a column is hidden. Mapped to LITERAL Tailwind classes in the
/// renderer — never composed from a variable — so the theme's `@source "../src"` scan
/// can see them. A caller-supplied class string would never reach the compiled CSS.
/// </summary>
public enum ResponsiveBreakpoint { None = 0, Sm, Md, Lg, Xl }

public delegate bool GridValueParser<TKey>(string raw, out TKey parsed);

/// <summary>Non-generic value parser (built once per column with TKey known) used by SQL push-down sources.</summary>
public delegate bool GridFilterValueParser(string raw, out object? parsed);
