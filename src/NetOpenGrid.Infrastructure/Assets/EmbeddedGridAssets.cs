using System.Reflection;
using System.Security.Cryptography;

namespace NetOpenGrid.Infrastructure.Assets;

public sealed record EmbeddedAsset(byte[] Bytes, string Version, string ContentType);

/// <summary>
/// Client runtime, vendor libraries and compiled theme stylesheets embedded in the
/// assembly, loaded once per process. The version (short SHA-256) enables immutable
/// caching via ?v= URLs.
/// </summary>
public static class EmbeddedGridAssets
{
    private const string ResourceRoot = "NetOpenGrid.Infrastructure.Assets.";
    private const string ThemeResourcePrefix = ResourceRoot + "css.netopengrid-";
    private const string CssExtension = ".css";
    private const string JavaScriptContentType = "text/javascript; charset=utf-8";
    private const string CssContentType = "text/css; charset=utf-8";

    public static readonly EmbeddedAsset ClientRuntime = Load("netopengrid.js", JavaScriptContentType);
    public static readonly EmbeddedAsset Htmx = Load("vendor/htmx.min.js", JavaScriptContentType);
    public static readonly EmbeddedAsset Alpine = Load("vendor/alpine.min.js", JavaScriptContentType);

    /// <summary>
    /// Compiled Tailwind themes keyed by theme name (the value <c>GridOptions.Theme</c>
    /// takes), with the <c>netopengrid-</c> prefix and <c>.css</c> suffix stripped.
    /// Adding a file to <c>themes/</c> makes it appear here with no code change.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, EmbeddedAsset> Themes = LoadThemes();

    private static EmbeddedAsset Load(string relativePath, string contentType)
    {
        var assembly = typeof(EmbeddedGridAssets).Assembly;
        var logicalName = ResourceRoot + relativePath.Replace('/', '.');

        return LoadResource(assembly, logicalName, contentType);
    }

    private static IReadOnlyDictionary<string, EmbeddedAsset> LoadThemes()
    {
        var assembly = typeof(EmbeddedGridAssets).Assembly;
        var themes = new Dictionary<string, EmbeddedAsset>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ThemeResourcePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(CssExtension, StringComparison.Ordinal))
            {
                continue;
            }

            var theme = resourceName[ThemeResourcePrefix.Length..^CssExtension.Length];
            themes[theme] = LoadResource(assembly, resourceName, CssContentType);
        }

        return themes;
    }

    private static EmbeddedAsset LoadResource(Assembly assembly, string logicalName, string contentType)
    {
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded asset '{logicalName}' is missing from the assembly.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var version = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();

        return new EmbeddedAsset(bytes, version, contentType);
    }
}
