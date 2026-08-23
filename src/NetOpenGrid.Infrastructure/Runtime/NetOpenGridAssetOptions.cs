namespace NetOpenGrid.Infrastructure.Runtime;

/// <summary>
/// Controls where the component's client runtime is served from and where the
/// app-compiled Tailwind theme CSS lives. The JS runtime and vendor libraries are
/// embedded in the assembly and served by <c>MapNetOpenGrid</c> automatically.
/// </summary>
public sealed class NetOpenGridAssetOptions
{
    public string AssetPrefix { get; set; } = "/_netgrid";
    public string CssPath { get; set; } = "/css";
    public string CssFilePrefix { get; set; } = "netopengrid-";

    internal string NormalizedAssetPrefix => Normalize(AssetPrefix);
    internal string NormalizedCssPath => Normalize(CssPath);

    private static string Normalize(string path)
    {
        var trimmed = path.TrimEnd('/');
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }
}
