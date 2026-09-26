using System.Globalization;
using System.Text;
using NetOpenGrid.Domain.Columns;

namespace NetOpenGrid.Infrastructure.Export;

/// <summary>
/// RFC-4180 CSV builder: cell values come from the column formatter (never RawCellHtml),
/// quotes/commas/newlines are escaped, header row uses the column headers.
/// Cells that a spreadsheet would evaluate as a formula (<c>= + - @</c>, tab, CR) are prefixed
/// with <c>'</c> (OWASP "CSV injection"); plain numbers such as <c>-12.5</c> are left alone.
/// </summary>
public static class GridCsvExporter
{
    /// <summary>UTF-8 with BOM: without it Excel opens the file as ANSI and mangles accents.</summary>
    public static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private const string NewLine = "\r\n";

    public static string Build<T>(IReadOnlyList<GridColumn<T>> columns, IReadOnlyList<T> rows)
    {
        var sb = new StringBuilder(4096);
        using var writer = new StringWriter(sb, CultureInfo.InvariantCulture);

        WriteHeader(writer, columns);
        WriteRows(writer, columns, rows);

        return sb.ToString();
    }

    public static void WriteHeader<T>(TextWriter writer, IReadOnlyList<GridColumn<T>> columns)
    {
        // Label, no Header: el encabezado puede ir en blanco y un CSV sin nombre de campo no se puede leer.
        WriteLine(writer, columns.Select(static c => c.Label));
    }

    public static void WriteRows<T>(TextWriter writer, IReadOnlyList<GridColumn<T>> columns, IEnumerable<T> rows)
    {
        foreach (var row in rows)
        {
            WriteLine(writer, columns.Select(c => c.Format(row) ?? string.Empty));
        }
    }

    private static void WriteLine(TextWriter writer, IEnumerable<string> cells)
    {
        var first = true;
        foreach (var cell in cells)
        {
            if (!first)
            {
                writer.Write(',');
            }

            writer.Write(Escape(cell));
            first = false;
        }

        writer.Write(NewLine);
    }

    internal static string Escape(string value)
    {
        if (IsFormulaLike(value))
        {
            value = "'" + value;
        }

        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');

        return needsQuotes
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static bool IsFormulaLike(string value) =>
        value.Length > 0 &&
        value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' &&
        !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
