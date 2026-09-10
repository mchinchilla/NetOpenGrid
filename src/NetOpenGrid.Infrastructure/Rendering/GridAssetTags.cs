using System.Text;
using System.Text.Encodings.Web;
using NetOpenGrid.Infrastructure.Runtime;

namespace NetOpenGrid.Infrastructure.Rendering;

/// <summary>
/// Head tags a host page renders ONCE, regardless of how many grids it embeds.
/// The fragment deliberately does not carry these: two grids on one page would
/// otherwise load htmx and Alpine twice.
/// </summary>
public static class GridAssetTags
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;

    public static string Head(NetOpenGridAssetOptions assetOptions, string theme)
    {
        ArgumentNullException.ThrowIfNull(assetOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(theme);

        var prefix = assetOptions.NormalizedAssetPrefix;
        var sb = new StringBuilder(512);

        // theme is written into an href attribute below; encode it the same way
        // GridHtmlRenderer.AppendDocumentStart does for the identical pattern
        // (the raw value is still used for the Themes dictionary lookup, which
        // isn't an HTML-injection sink).
        var encodedTheme = Encoder.Encode(theme);

        sb.Append("<link rel=\"stylesheet\" href=\"");
        if (assetOptions.NormalizedCssPath is { } cssPath)
        {
            sb.Append(cssPath).Append('/').Append(assetOptions.CssFilePrefix).Append(encodedTheme).Append(".css");
        }
        else
        {
            sb.Append(prefix).Append("/css/netopengrid-").Append(encodedTheme).Append(".css");
            if (Assets.EmbeddedGridAssets.Themes.TryGetValue(theme, out var themeAsset))
            {
                sb.Append("?v=").Append(themeAsset.Version);
            }
        }
        sb.Append("\">");

        sb.Append("<style>[x-cloak]{display:none!important}</style>");
        sb.Append("<script src=\"").Append(prefix).Append("/netopengrid.js?v=")
          .Append(Assets.EmbeddedGridAssets.ClientRuntime.Version).Append("\" defer></script>");

        return sb.ToString();
    }
}
