using System.Collections.ObjectModel;
using NetOpenGrid.Application.Options;

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
    /// The data-endpoint route prefix (<see cref="NetOpenGrid.Infrastructure.Endpoints.NetOpenGridEndpointOptions.Prefix"/>),
    /// published to the client so <c>netopengrid.js</c> builds its fetch/navigation URLs from it
    /// instead of the hardcoded default. <c>MapNetOpenGrid</c> is the single place that knows both
    /// values and keeps them in sync; it assigns this before mapping any endpoint.
    /// </summary>
    public string RoutePrefix { get; set; } = "/netgrid";

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

    private string? _brandColor;

    /// <summary>
    /// The host's brand colour, as the <c>brand-600</c> step (primary buttons, links, focus rings).
    /// Every embedded theme mixes its lighter and darker steps from it, so the grid takes the
    /// host's identity without a stylesheet of its own. Rendered as
    /// <c>&lt;style&gt;:root{--ng-brand:…}&lt;/style&gt;</c> right after the theme link, in every
    /// grid document and in <see cref="Rendering.GridAssetTags.Head"/>. <c>null</c> (the default)
    /// or blank keeps the theme's own palette. A grid can override it with <c>WithBrandColor</c>.
    /// Accepts <c>#rrggbb</c>, a named colour or one colour function (<c>oklch(0.6 0.118 185)</c>);
    /// anything else throws <see cref="NetOpenGrid.Domain.GridConfigurationException"/>.
    /// </summary>
    public string? BrandColor
    {
        get => _brandColor;
        set => _brandColor = string.IsNullOrWhiteSpace(value) ? null : GridCssColor.Validate(value);
    }

    /// <summary>
    /// Stylesheets every grid document (and <see cref="Rendering.GridAssetTags.Head"/>) links
    /// AFTER the theme, so small overrides win without replacing it (<see cref="CssPath"/> does
    /// that). Relative or http(s) URLs only: anything else throws when it is added. They load in
    /// the grid's own document, also when it is embedded in an iframe. A grid adds its own with
    /// <c>WithStylesheet</c>.
    /// </summary>
    public IList<string> ExtraStylesheets { get; } = new StylesheetUrlList();

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

    /// <summary>A plain list that refuses unsafe URLs as they are added, so a typo fails at startup.</summary>
    private sealed class StylesheetUrlList : Collection<string>
    {
        protected override void InsertItem(int index, string item) =>
            base.InsertItem(index, GridUrl.ValidateStylesheet(item));

        protected override void SetItem(int index, string item) =>
            base.SetItem(index, GridUrl.ValidateStylesheet(item));
    }
}
