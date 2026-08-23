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
