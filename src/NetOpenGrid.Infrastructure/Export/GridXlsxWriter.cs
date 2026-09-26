using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NetOpenGrid.Domain.Columns;

namespace NetOpenGrid.Infrastructure.Export;

/// <summary>
/// Streaming xlsx (SpreadsheetML) writer with no third-party dependency: one worksheet, written page
/// by page into the zip while rows arrive, so memory stays bounded by one page.
/// <list type="bullet">
/// <item>Typed cells from <see cref="GridColumn{T}.RawValue"/>: numbers, dates (Excel serials with a
/// date format), booleans; everything else is an inline string, which Excel never evaluates as a
/// formula (no CSV-style injection).</item>
/// <item>Bold, frozen header row with an autofilter; column widths estimated from the first page.</item>
/// <item>Per-column number formats via <see cref="GridColumn{T}.ExcelFormat"/>.</item>
/// </list>
/// </summary>
public static class GridXlsxWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Excel's hard row limit (1,048,576) minus the header row.</summary>
    public const int MaxDataRows = 1_048_575;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly DateTime ExcelEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    // Fixed cellXfs indexes; per-column custom formats are appended after these.
    private const int StyleHeader = 1;
    private const int StyleDate = 2;
    private const int StyleDateTime = 3;
    private const int FirstCustomStyle = 4;

    public static async Task WriteAsync<T>(
        Stream output,
        string sheetName,
        IReadOnlyList<GridColumn<T>> columns,
        IAsyncEnumerable<IReadOnlyList<T>> pages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(pages);

        var customFormats = columns
            .Select(static c => c.ExcelFormat)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var columnStyles = columns
            .Select(c => c.ExcelFormat is { } format ? FirstCustomStyle + Array.IndexOf(customFormats, format) : 0)
            .ToArray();
        var letters = columns.Select((_, i) => ColumnLetter(i)).ToArray();
        var safeSheetName = SheetName(sheetName);

        var buffered = new SyncWriteBufferStream(output);
        var rowCount = 0;

        await using (var zip = await ZipArchive.CreateAsync(buffered, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
        {
            // The sheet goes first: the workbook's filter range needs the final row count.
            var sheet = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
            await using (var sheetStream = await sheet.OpenAsync(cancellationToken))
            await using (var writer = new StreamWriter(sheetStream, Utf8, bufferSize: 64 * 1024))
            {
                var sb = new StringBuilder(64 * 1024);
                var headerWritten = false;

                await foreach (var page in pages.WithCancellation(cancellationToken))
                {
                    if (!headerWritten)
                    {
                        AppendSheetStart(sb, columns, page);
                        AppendHeaderRow(sb, columns, letters);
                        headerWritten = true;
                    }

                    foreach (var item in page)
                    {
                        rowCount++;
                        AppendRow(sb, columns, letters, columnStyles, item, rowCount + 1);
                    }

                    await writer.WriteAsync(sb, cancellationToken);
                    sb.Clear();
                }

                if (!headerWritten)
                {
                    AppendSheetStart(sb, columns, []);
                    AppendHeaderRow(sb, columns, letters);
                }

                sb.Append("</sheetData>");
                sb.Append("<autoFilter ref=\"A1:").Append(letters[^1]).Append(rowCount + 1).Append("\"/>");
                sb.Append("</worksheet>");
                await writer.WriteAsync(sb, cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }

            await WriteEntryAsync(zip, "[Content_Types].xml", ContentTypes, cancellationToken);
            await WriteEntryAsync(zip, "_rels/.rels", RootRels, cancellationToken);
            await WriteEntryAsync(zip, "xl/_rels/workbook.xml.rels", WorkbookRels, cancellationToken);
            await WriteEntryAsync(zip, "xl/workbook.xml", Workbook(safeSheetName, letters[^1], rowCount + 1), cancellationToken);
            await WriteEntryAsync(zip, "xl/styles.xml", Styles(customFormats), cancellationToken);
        }

        await buffered.FlushAsync(cancellationToken);
    }

    private static void AppendSheetStart<T>(StringBuilder sb, IReadOnlyList<GridColumn<T>> columns, IReadOnlyList<T> sample)
    {
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        sb.Append("<cols>");
        for (var i = 0; i < columns.Count; i++)
        {
            var width = Math.Clamp(EstimateWidth(columns[i], sample), 8, 60);
            sb.Append("<col min=\"").Append(i + 1).Append("\" max=\"").Append(i + 1)
              .Append("\" width=\"").Append(width.ToString(CultureInfo.InvariantCulture)).Append("\" customWidth=\"1\"/>");
        }

        sb.Append("</cols><sheetData>");
    }

    /// <summary>Characters of the longest formatted value in the first page (or the header), plus padding.</summary>
    private static double EstimateWidth<T>(GridColumn<T> column, IReadOnlyList<T> sample)
    {
        var longest = column.Label.Length;
        foreach (var item in sample)
        {
            var length = column.Format(item)?.Length ?? 0;
            if (length > longest)
            {
                longest = length;
            }
        }

        return longest + 3;
    }

    private static void AppendHeaderRow<T>(StringBuilder sb, IReadOnlyList<GridColumn<T>> columns, string[] letters)
    {
        sb.Append("<row r=\"1\">");
        for (var i = 0; i < columns.Count; i++)
        {
            AppendInlineString(sb, letters[i] + "1", columns[i].Label, StyleHeader);
        }

        sb.Append("</row>");
    }

    private static void AppendRow<T>(StringBuilder sb, IReadOnlyList<GridColumn<T>> columns, string[] letters, int[] styles, T item, int rowNumber)
    {
        sb.Append("<row r=\"").Append(rowNumber).Append("\">");
        for (var i = 0; i < columns.Count; i++)
        {
            var reference = letters[i] + rowNumber.ToString(CultureInfo.InvariantCulture);
            AppendCell(sb, reference, columns[i], item, styles[i]);
        }

        sb.Append("</row>");
    }

    private static void AppendCell<T>(StringBuilder sb, string reference, GridColumn<T> column, T item, int style)
    {
        var raw = column.RawValue is { } rawValue ? rawValue(item) : null;

        switch (raw)
        {
            case null:
                var text = column.RawValue is null ? column.Format(item) : null;
                if (!string.IsNullOrEmpty(text))
                {
                    AppendInlineString(sb, reference, text, style);
                }

                return;
            case bool b:
                sb.Append("<c r=\"").Append(reference).Append("\" t=\"b\"><v>").Append(b ? '1' : '0').Append("</v></c>");
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                AppendNumber(sb, reference, Convert.ToDecimal(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), style);
                return;
            case double or float:
                var d = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                if (double.IsFinite(d))
                {
                    AppendNumber(sb, reference, d.ToString("R", CultureInfo.InvariantCulture), style);
                }

                return;
            case DateOnly date:
                AppendDate(sb, reference, date.ToDateTime(TimeOnly.MinValue), style == 0 ? StyleDate : style);
                return;
            case DateTime dateTime:
                AppendDate(sb, reference, dateTime, style == 0 ? (dateTime.TimeOfDay == TimeSpan.Zero ? StyleDate : StyleDateTime) : style);
                return;
            case DateTimeOffset offset:
                AppendDate(sb, reference, offset.DateTime, style == 0 ? StyleDateTime : style);
                return;
            case JsonElement json:
                AppendJson(sb, reference, json, style);
                return;
            case Enum:
            case string:
            default:
                // Enums, strings and anything else: the column's display text.
                AppendInlineString(sb, reference, column.Format(item), style);
                return;
        }
    }

    private static void AppendJson(StringBuilder sb, string reference, JsonElement json, int style)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Number when json.TryGetDecimal(out var number):
                AppendNumber(sb, reference, number.ToString(CultureInfo.InvariantCulture), style);
                return;
            case JsonValueKind.True or JsonValueKind.False:
                sb.Append("<c r=\"").Append(reference).Append("\" t=\"b\"><v>").Append(json.ValueKind == JsonValueKind.True ? '1' : '0').Append("</v></c>");
                return;
            case JsonValueKind.String:
                AppendInlineString(sb, reference, json.GetString(), style);
                return;
            case JsonValueKind.Null or JsonValueKind.Undefined:
                return;
            default:
                AppendInlineString(sb, reference, json.GetRawText(), style);
                return;
        }
    }

    private static void AppendDate(StringBuilder sb, string reference, DateTime value, int style)
    {
        var serial = (value - ExcelEpoch).TotalDays;
        if (serial < 1)
        {
            return;   // Excel cannot show dates before 1900-01-01
        }

        AppendNumber(sb, reference, serial.ToString("R", CultureInfo.InvariantCulture), style);
    }

    private static void AppendNumber(StringBuilder sb, string reference, string value, int style)
    {
        sb.Append("<c r=\"").Append(reference).Append('"');
        if (style != 0)
        {
            sb.Append(" s=\"").Append(style).Append('"');
        }

        sb.Append("><v>").Append(value).Append("</v></c>");
    }

    private static void AppendInlineString(StringBuilder sb, string reference, string? value, int style)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        sb.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"");
        if (style != 0)
        {
            sb.Append(" s=\"").Append(style).Append('"');
        }

        sb.Append("><is><t xml:space=\"preserve\">");
        AppendXmlText(sb, value.Length > 32_767 ? value[..32_767] : value);   // Excel's per-cell limit
        sb.Append("</t></is></c>");
    }

    /// <summary>Escapes XML and drops the control characters XML 1.0 cannot carry (keeps tab, CR, LF).</summary>
    private static void AppendXmlText(StringBuilder sb, string value)
    {
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\t' or '\n' or '\r': sb.Append(ch); break;
                case < ' ' or '￾' or '￿': break;
                default: sb.Append(ch); break;
            }
        }
    }

    internal static string ColumnLetter(int index)
    {
        var letters = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            letters = (char)('A' + (n - 1) % 26) + letters;
        }

        return letters;
    }

    /// <summary>Excel sheet names: at most 31 characters, none of <c>: \ / ? * [ ]</c>, not blank.</summary>
    internal static string SheetName(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Where(static c => c is not (':' or '\\' or '/' or '?' or '*' or '[' or ']') && !char.IsControl(c)).ToArray()).Trim().Trim('\'');
        if (cleaned.Length == 0)
        {
            cleaned = "Sheet1";
        }

        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string content, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await stream.WriteAsync(Utf8.GetBytes(content), cancellationToken);
    }

    private static string Workbook(string sheetName, string lastColumn, int lastRow)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        sb.Append("<sheets><sheet name=\"");
        AppendXmlText(sb, sheetName);
        sb.Append("\" sheetId=\"1\" r:id=\"rId1\"/></sheets>");
        sb.Append("<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">'");
        AppendXmlText(sb, sheetName.Replace("'", "''", StringComparison.Ordinal));
        sb.Append("'!$A$1:$").Append(lastColumn).Append('$').Append(lastRow).Append("</definedName></definedNames>");
        sb.Append("</workbook>");
        return sb.ToString();
    }

    private static string Styles(string[] customFormats)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<numFmts count=\"").Append(2 + customFormats.Length).Append("\">");
        sb.Append("<numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd\"/>");
        sb.Append("<numFmt numFmtId=\"165\" formatCode=\"yyyy-mm-dd hh:mm\"/>");
        for (var i = 0; i < customFormats.Length; i++)
        {
            sb.Append("<numFmt numFmtId=\"").Append(166 + i).Append("\" formatCode=\"");
            AppendXmlText(sb, customFormats[i]);
            sb.Append("\"/>");
        }

        sb.Append("</numFmts>");
        sb.Append("<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>");
        sb.Append("<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>");
        sb.Append("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
        sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        sb.Append("<cellXfs count=\"").Append(FirstCustomStyle + customFormats.Length).Append("\">");
        sb.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>");
        sb.Append("<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>");
        sb.Append("<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>");
        sb.Append("<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>");
        for (var i = 0; i < customFormats.Length; i++)
        {
            sb.Append("<xf numFmtId=\"").Append(166 + i).Append("\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>");
        }

        sb.Append("</cellXfs>");
        sb.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
        sb.Append("</styleSheet>");
        return sb.ToString();
    }

    private const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private const string RootRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string WorkbookRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";
}
