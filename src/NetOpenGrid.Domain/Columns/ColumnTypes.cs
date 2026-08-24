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

public delegate bool GridValueParser<TKey>(string raw, out TKey parsed);

/// <summary>Non-generic value parser (built once per column with TKey known) used by SQL push-down sources.</summary>
public delegate bool GridFilterValueParser(string raw, out object? parsed);
