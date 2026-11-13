using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Infrastructure.Assets;
using NetOpenGrid.Infrastructure.Binding;
using NetOpenGrid.Infrastructure.Runtime;

namespace NetOpenGrid.Infrastructure.Endpoints;

public sealed class NetOpenGridEndpointOptions
{
    public string Prefix { get; set; } = "/netgrid";
}

public static class NetOpenGridEndpointExtensions
{
    public static RouteGroupBuilder MapNetOpenGrid(
        this IEndpointRouteBuilder endpoints,
        Action<NetOpenGridEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new NetOpenGridEndpointOptions();
        configure?.Invoke(options);

        var assetOptions = endpoints.ServiceProvider.GetService<NetOpenGridAssetOptions>() ?? new NetOpenGridAssetOptions();
        var prefix = options.Prefix.TrimEnd('/');
        assetOptions.RoutePrefix = prefix;
        var assetPrefix = assetOptions.NormalizedAssetPrefix;

        // Assets stay anonymous and outside the returned group.
        MapAsset(endpoints, $"{assetPrefix}/netopengrid.js", EmbeddedGridAssets.ClientRuntime);
        MapAsset(endpoints, $"{assetPrefix}/vendor/htmx.min.js", EmbeddedGridAssets.Htmx);
        MapAsset(endpoints, $"{assetPrefix}/vendor/alpine.min.js", EmbeddedGridAssets.Alpine);

        foreach (var (theme, asset) in EmbeddedGridAssets.Themes)
        {
            MapAsset(endpoints, $"{assetPrefix}/css/netopengrid-{theme}.css", asset);
        }

        var data = endpoints.MapGroup(prefix);

        data.MapGet("/{gridId}/rows", async (string gridId, HttpContext http, CancellationToken cancellationToken) =>
        {
            if (http.RequestServices.GetKeyedService<IGridRuntime>(gridId) is not { } runtime)
            {
                return Results.NotFound($"Unknown grid '{gridId}'.");
            }

            var response = await runtime.RenderRowsAsync(http.Request.ToGridRequestValues(), cancellationToken);
            ApplyMetaHeaders(http, response);

            return Results.Text(response.Html, "text/html; charset=utf-8");
        });

        data.MapGet("/{gridId}/values", async (string gridId, HttpContext http, CancellationToken cancellationToken) =>
        {
            if (http.RequestServices.GetKeyedService<IGridRuntime>(gridId) is not { } runtime)
            {
                return Results.NotFound($"Unknown grid '{gridId}'.");
            }

            var field = http.Request.Query["field"].ToString();
            var json = await runtime.RenderValuesAsync(field, http.Request.ToGridRequestValues(), cancellationToken);
            http.Response.Headers.CacheControl = "no-store";

            return Results.Content(json, "application/json; charset=utf-8");
        });

        data.MapGet("/{gridId}/export", async (string gridId, HttpContext http, CancellationToken cancellationToken) =>
        {
            if (http.RequestServices.GetKeyedService<IGridRuntime>(gridId) is not { } runtime)
            {
                return Results.NotFound($"Unknown grid '{gridId}'.");
            }

            var format = http.Request.Query["format"].ToString().ToLowerInvariant() switch
            {
                "" or "csv" => GridExportFormats.Csv,
                "xlsx" => GridExportFormats.Xlsx,
                _ => GridExportFormats.None
            };

            if (format == GridExportFormats.None || (runtime.ExportFormats & format) == 0)
            {
                return Results.Text("This export format is not available for this grid.", "text/plain; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
            }

            var export = await runtime.CreateExportAsync(http.Request.ToGridRequestValues(), format, cancellationToken);
            http.Response.Headers.CacheControl = "no-store";

            if (export.ExceedsLimit)
            {
                return Results.Text(export.LimitMessage, "text/plain; charset=utf-8", statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            return Results.Stream(
                output => export.WriteToAsync(output, cancellationToken),
                export.ContentType,
                fileDownloadName: export.FileName);
        });

        data.MapGet("/{gridId}", async (string gridId, HttpContext http, CancellationToken cancellationToken) =>
        {
            if (http.RequestServices.GetKeyedService<IGridRuntime>(gridId) is not { } runtime)
            {
                return Results.NotFound($"Unknown grid '{gridId}'.");
            }

            var html = await runtime.RenderShellAsync(http.Request.ToGridRequestValues(), cancellationToken);
            return Results.Text(html, "text/html; charset=utf-8");
        });

        return data;
    }

    private static void MapAsset(IEndpointRouteBuilder endpoints, string pattern, EmbeddedAsset asset)
    {
        endpoints.MapGet(pattern, (HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(asset.Bytes, asset.ContentType);
        });
    }

    private static void ApplyMetaHeaders(HttpContext http, GridRowsResponse response)
    {
        var headers = http.Response.Headers;
        headers["X-Grid-Total"] = response.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        headers["X-Grid-Page"] = response.Page.ToString(System.Globalization.CultureInfo.InvariantCulture);
        headers["X-Grid-Page-Size"] = response.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        headers["X-Grid-Page-Count"] = response.PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (response.TotalCountIsCapped)
        {
            headers["X-Grid-Total-Capped"] = "1";
        }

        // Filter value labels travel next to the rest of the rows' metadata, as ASCII-only JSON
        // (see GridFilterLabels.ToJson). The body stays what it is, a run of <tr>: it is swapped
        // into the <tbody>, cached block by block by the virtual scroll and handed to hosts as
        // GridRowsResponse.Html, and none of those should have to strip a <script> out of it.
        if (response.FilterLabels.Count > 0)
        {
            var json = GridFilterLabels.ToJson(response.FilterLabels);
            if (json.Length <= GridFilterLabels.MaxHeaderLength)
            {
                headers[GridFilterLabels.HeaderName] = json;
            }
            else
            {
                http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("NetOpenGrid").LogWarning(
                    "NetOpenGrid: the filter value labels take {Length} characters, above the {Limit} a response header may carry; the filter chips show the raw values.",
                    json.Length,
                    GridFilterLabels.MaxHeaderLength);
            }
        }

        if (response.InvalidFilters.Count > 0)
        {
            // Field names may be anything a JSON payload has: same ASCII-only JSON as the labels.
            headers[GridFilterLabels.InvalidFiltersHeaderName] = System.Text.Json.JsonSerializer.Serialize(response.InvalidFilters);
        }

        headers["Cache-Control"] = "no-store";
    }
}
