namespace NetOpenGrid.Infrastructure.Runtime;

/// <summary>
/// Controls where the component's client runtime and theme stylesheet are served from.
/// The JS runtime, vendor libraries and compiled themes are all embedded in the assembly
/// and served by <c>MapNetOpenGrid</c> automatically.
/// </summary>
public sealed class NetOpenGridAssetOptions
{
    /// <summary>Route prefix the embedded assets are served under.</summary>
    public string AssetPrefix { get; set; } = "/_netgrid";

    /// <summary>
    /// When <c>null</c> (the default) the theme compiled into the assembly is served from
    /// <see cref="AssetPrefix"/>/css, so the host application needs no stylesheet of its own.
    /// Set this to serve your own stylesheet from the host application instead
    /// (for example <c>"/css"</c>), in which case <see cref="CssFilePrefix"/> applies.
    /// </summary>
    public string? CssPath { get; set; }

    /// <summary>
    /// File name prefix for a self-hosted stylesheet. Applies only when <see cref="CssPath"/>
    /// is set: it cannot rename a stylesheet compiled into the assembly.
    /// </summary>
    public string CssFilePrefix { get; set; } = "netopengrid-";

    internal string NormalizedAssetPrefix => Normalize(AssetPrefix);
    internal string? NormalizedCssPath => CssPath is null ? null : Normalize(CssPath);

    private static string Normalize(string path)
    {
        var trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0)
        {
            // "/", "" and "//" all normalize to the root: return "" rather than "/" so that
            // concatenating it with a leading "/segment" produces "/segment", not "//segment"
            // (which a browser resolves as a scheme-relative URL to an external host).
            return string.Empty;
        }

        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }
}
