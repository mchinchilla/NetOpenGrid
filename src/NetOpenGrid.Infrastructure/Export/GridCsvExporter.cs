using System.Text;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Columns;

namespace NetOpenGrid.Infrastructure.Export;

/// <summary>
/// RFC-4180 CSV builder: cell values come from the column formatter (never RawCellHtml),
/// quotes/commas/newlines are escaped, header row uses the column headers.
/// </summary>
public static class GridCsvExporter
{
    public static string Build<T>(IReadOnlyList<GridColumn<T>> columns, IReadOnlyList<T> rows)
    {
        var sb = new StringBuilder(4096);

        // Label, no Header: el encabezado puede ir en blanco y un CSV sin nombre de campo no se puede leer.
        AppendLine(sb, string.Join(",", columns.Select(static c => Escape(c.Label))));

        foreach (var row in rows)
        {
            AppendLine(sb, string.Join(",", columns.Select(c => Escape(c.Format(row) ?? string.Empty))));
        }

        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, string line)
    {
        sb.Append(line);
        sb.Append("\r\n");
    }

    private static string Escape(string value)
    {
        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');

        return needsQuotes
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
