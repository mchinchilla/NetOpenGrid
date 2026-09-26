namespace NetOpenGrid.Infrastructure.Runtime;

/// <summary>
/// Client-facing UI strings. Ships English defaults plus a Spanish preset
/// (<c>UseCulture("es")</c>); every key can be overridden for custom locales.
/// The effective dictionary is injected into the shell as
/// <c>__NETGRID__.locale</c> so the JS runtime uses the exact same strings.
/// </summary>
public sealed class NetOpenGridLocalizationOptions
{
    private const string DefaultCulture = "en";
    private readonly Dictionary<string, string> _strings;

    public NetOpenGridLocalizationOptions()
    {
        _strings = new Dictionary<string, string>(Presets[DefaultCulture], StringComparer.Ordinal);
    }

    public string this[string key] => _strings.GetValueOrDefault(key, key);

    public IReadOnlyDictionary<string, string> Strings => _strings;

    /// <summary>Overrides a single key.</summary>
    public NetOpenGridLocalizationOptions Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _strings[key] = value;
        return this;
    }

    /// <summary>Loads a built-in preset ("en", "es") keeping any overrides applied before this call.</summary>
    public NetOpenGridLocalizationOptions UseCulture(string culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        var preset = Presets.GetValueOrDefault(culture, Presets[DefaultCulture]);

        foreach (var (key, value) in preset)
        {
            _strings[key] = value;
        }

        return this;
    }

    public static IReadOnlyDictionary<string, string> PresetFor(string culture) =>
        Presets.GetValueOrDefault(culture, Presets[DefaultCulture]);

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Presets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new Dictionary<string, string>
        {
            ["search.placeholder"] = "Search...",
            ["search.aria"] = "Search",
            ["theme.aria"] = "Toggle color theme",
            ["export.aria"] = "Export filtered rows",
            ["export.csv"] = "CSV (.csv)",
            ["export.xlsx"] = "Excel (.xlsx)",
            ["pager.rowsPerPage"] = "Rows per page",
            ["pager.prev"] = "Prev",
            ["pager.next"] = "Next",
            ["records.one"] = "1 record",
            ["records.many"] = "{n} records",
            ["range.empty"] = "No records",
            ["range.format"] = "{from}-{to} of {total}",
            ["filter.values.search"] = "Filter values...",
            ["filter.values.selectAll"] = "Select all",
            ["filter.values.loading"] = "Loading...",
            ["filter.values.truncated"] = "Showing {shown} of {total} values",
            ["filter.value.placeholder"] = "Value",
            ["filter.apply"] = "Apply",
            ["filter.clear"] = "Clear",
            ["filter.aria"] = "Filter {field}",
            ["pin.aria"] = "Pin column {field}",
            ["pin.left.aria"] = "Pin column {field} to the left",
            ["pin.right.aria"] = "Pin column {field} to the right",
            ["pin.none.aria"] = "Unpin column {field}",
            ["group.placeholder"] = "Group by...",
            ["group.aria"] = "Group by",
            ["group.blank"] = "(Blanks)",
            ["group.toggle.aria"] = "Toggle group",
            ["group.panel.hint"] = "Drag a column header here to group by it",
            ["group.remove.aria"] = "Remove grouping by {field}",
            ["agg.sum"] = "Sum",
            ["agg.avg"] = "Avg",
            ["agg.min"] = "Min",
            ["agg.max"] = "Max",
            ["agg.total"] = "Total",
            ["agg.subtotal"] = "Subtotal",
            ["columns.aria"] = "Show or hide columns",
            ["columns.title"] = "Columns",
            ["columns.reset"] = "Show all",
            ["actions.header"] = "Actions",
            ["views.aria"] = "Saved views",
            ["views.title"] = "Views",
            ["views.default"] = "Default view",
            ["views.shared"] = "Predefined",
            ["views.mine"] = "My views",
            ["views.empty"] = "No saved views yet.",
            ["views.placeholder"] = "Name this view",
            ["views.save"] = "Save",
            ["views.delete.aria"] = "Delete view {name}",
            ["select.all.aria"] = "Select all on page",
            ["select.row.aria"] = "Select row",
            ["select.selected.one"] = "1 selected",
            ["select.selected.many"] = "{n} selected",
            ["select.export"] = "Export CSV",
            ["select.clear"] = "Clear",
            ["chips.anyOf"] = "any of {values}",
            ["chips.anyOfMore"] = "any of {values} +{n}",
            ["ops.equals"] = "equals",
            ["ops.not-equals"] = "not equals",
            ["ops.contains"] = "contains",
            ["ops.starts-with"] = "starts with",
            ["ops.ends-with"] = "ends with",
            ["ops.gt"] = "greater than",
            ["ops.gte"] = "greater or equal",
            ["ops.lt"] = "less than",
            ["ops.lte"] = "less or equal",
            ["ops.is-empty"] = "is empty",
            ["ops.is-not-empty"] = "is not empty"
        },
        ["es"] = new Dictionary<string, string>
        {
            ["search.placeholder"] = "Buscar...",
            ["search.aria"] = "Buscar",
            ["theme.aria"] = "Cambiar tema de color",
            ["export.aria"] = "Exportar filas filtradas",
            ["export.csv"] = "CSV (.csv)",
            ["export.xlsx"] = "Excel (.xlsx)",
            ["pager.rowsPerPage"] = "Filas por página",
            ["pager.prev"] = "Anterior",
            ["pager.next"] = "Siguiente",
            ["records.one"] = "1 registro",
            ["records.many"] = "{n} registros",
            ["range.empty"] = "Sin registros",
            ["range.format"] = "{from}-{to} de {total}",
            ["filter.values.search"] = "Filtrar valores...",
            ["filter.values.selectAll"] = "Seleccionar todo",
            ["filter.values.loading"] = "Cargando...",
            ["filter.values.truncated"] = "Mostrando {shown} de {total} valores",
            ["filter.value.placeholder"] = "Valor",
            ["filter.apply"] = "Aplicar",
            ["filter.clear"] = "Limpiar",
            ["filter.aria"] = "Filtrar {field}",
            ["pin.aria"] = "Fijar columna {field}",
            ["pin.left.aria"] = "Fijar la columna {field} a la izquierda",
            ["pin.right.aria"] = "Fijar la columna {field} a la derecha",
            ["pin.none.aria"] = "Dejar de fijar la columna {field}",
            ["group.placeholder"] = "Agrupar por...",
            ["group.aria"] = "Agrupar por",
            ["group.blank"] = "(Vacíos)",
            ["group.toggle.aria"] = "Alternar grupo",
            ["group.panel.hint"] = "Arrastra aquí el encabezado de una columna para agrupar por ella",
            ["group.remove.aria"] = "Quitar agrupamiento por {field}",
            ["agg.sum"] = "Suma",
            ["agg.avg"] = "Prom.",
            ["agg.min"] = "Mín.",
            ["agg.max"] = "Máx.",
            ["agg.total"] = "Total",
            ["agg.subtotal"] = "Subtotal",
            ["columns.aria"] = "Mostrar u ocultar columnas",
            ["columns.title"] = "Columnas",
            ["columns.reset"] = "Mostrar todas",
            ["actions.header"] = "Acciones",
            ["views.aria"] = "Vistas guardadas",
            ["views.title"] = "Vistas",
            ["views.default"] = "Vista por defecto",
            ["views.shared"] = "Predefinidas",
            ["views.mine"] = "Mis vistas",
            ["views.empty"] = "Aún no hay vistas guardadas.",
            ["views.placeholder"] = "Nombre de la vista",
            ["views.save"] = "Guardar",
            ["views.delete.aria"] = "Borrar la vista {name}",
            ["select.all.aria"] = "Seleccionar todo en la página",
            ["select.row.aria"] = "Seleccionar fila",
            ["select.selected.one"] = "1 seleccionado",
            ["select.selected.many"] = "{n} seleccionados",
            ["select.export"] = "Exportar CSV",
            ["select.clear"] = "Limpiar",
            ["chips.anyOf"] = "alguno de {values}",
            ["chips.anyOfMore"] = "alguno de {values} +{n}",
            ["ops.equals"] = "igual a",
            ["ops.not-equals"] = "distinto de",
            ["ops.contains"] = "contiene",
            ["ops.starts-with"] = "comienza con",
            ["ops.ends-with"] = "termina con",
            ["ops.gt"] = "mayor que",
            ["ops.gte"] = "mayor o igual",
            ["ops.lt"] = "menor que",
            ["ops.lte"] = "menor o igual",
            ["ops.is-empty"] = "está vacío",
            ["ops.is-not-empty"] = "no está vacío"
        }
    };
}
