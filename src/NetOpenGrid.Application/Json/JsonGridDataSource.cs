using System.Text.Json;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;

namespace NetOpenGrid.Application.Json;

/// <summary>
/// Data source over JSON documents (arrays of objects).
/// Parsing happens once up-front; the request path walks <see cref="JsonElement"/> only.
/// </summary>
public sealed class JsonGridDataSource : InMemoryGridDataSource<JsonElement>
{
    public JsonGridDataSource(GridOptions<JsonElement> options, string json, JsonDocumentOptions documentOptions = default)
        : base(options, ParseArray(json, documentOptions))
    {
    }

    public JsonGridDataSource(GridOptions<JsonElement> options, IReadOnlyList<JsonElement> rows)
        : base(options, rows)
    {
    }

    public JsonGridDataSource(GridOptions<JsonElement> options, Func<CancellationToken, ValueTask<Stream>> streamLoader)
        : base(options, async ct =>
        {
            await using var stream = await streamLoader(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return Materialize(doc);
        })
    {
    }

    public static async ValueTask<JsonGridDataSource> FromFileAsync(
        GridOptions<JsonElement> options,
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return new JsonGridDataSource(options, Materialize(doc));
    }

    private static IReadOnlyList<JsonElement> ParseArray(string json, JsonDocumentOptions documentOptions)
    {
        using var doc = JsonDocument.Parse(json, documentOptions);
        return Materialize(doc);
    }

    private static IReadOnlyList<JsonElement> Materialize(JsonDocument doc)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new GridException("JSON grid data source requires a top-level array of objects.");
        }

        var rows = new List<JsonElement>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            rows.Add(element.Clone());
        }

        return rows;
    }
}
