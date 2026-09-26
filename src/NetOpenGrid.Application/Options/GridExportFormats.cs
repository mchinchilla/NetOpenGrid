namespace NetOpenGrid.Application.Options;

/// <summary>Formats offered by the built-in export (<c>GET /netgrid/{id}/export?format=...</c>).</summary>
[Flags]
public enum GridExportFormats
{
    /// <summary>No export button and no export endpoint for this grid.</summary>
    None = 0,
    Csv = 1,
    Xlsx = 2,
    All = Csv | Xlsx
}
