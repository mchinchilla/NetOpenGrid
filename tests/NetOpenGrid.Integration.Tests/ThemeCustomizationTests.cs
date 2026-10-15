using System.Text;
using System.Text.RegularExpressions;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;
using NetOpenGrid.Infrastructure.Assets;
using NetOpenGrid.Infrastructure.Rendering;
using NetOpenGrid.Infrastructure.Runtime;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>
/// Adapting the grid to a host's identity without replacing the theme: one brand colour,
/// extra stylesheets after the theme, and the semantic badge set every theme guarantees.
/// </summary>
public class ThemeCustomizationTests
{
    private sealed record Row(string Name);

    private static readonly string[] SemanticBadges =
        ["badge", "badge-success", "badge-warning", "badge-danger", "badge-info", "badge-neutral", "badge-brand"];

    // Product-specific names kept as aliases so existing RawCellHtml cells keep their colours.
    private static readonly string[] LegacyBadges =
        ["badge-muted", "badge-pending", "badge-shipped", "badge-delivered", "badge-cancelled"];

    private static GridOptions<Row> Options(Action<GridOptionsBuilder<Row>>? configure = null)
    {
        var builder = new GridOptionsBuilder<Row>().WithId("brand").AddColumn("name", r => r.Name);
        configure?.Invoke(builder);
        return builder.Build();
    }

    private static Task<string> ShellAsync(GridOptions<Row> options, NetOpenGridAssetOptions assets, bool embedded = false) =>
        new GridHtmlRenderer<Row>(options, assets)
            .RenderShellAsync(new GridExecutionResult<Row>(GridQuery.Empty, new PageResult<Row>([], 0, 1, 10)), embedded: embedded)
            .AsTask();

    private static string Head(string html) => html[..html.IndexOf("</head>", StringComparison.Ordinal)];

    private static int IndexOf(string html, string value)
    {
        var index = html.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{value}' not found in the document head");
        return index;
    }

    [Fact]
    public async Task Default_WritesNoBrandRuleAndNoExtraLinks()
    {
        var head = Head(await ShellAsync(Options(), new NetOpenGridAssetOptions()));

        Assert.DoesNotContain("--ng-brand", head);
        Assert.Single(Regex.Matches(head, "<link rel=\"stylesheet\""));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrandRuleAndStylesheets_FollowTheThemeLink_GlobalBeforeGrid(bool embedded)
    {
        var assets = new NetOpenGridAssetOptions { BrandColor = "#0d9488", ExtraStylesheets = { "/css/site.css" } };
        var options = Options(b => b.WithStylesheet("/css/grid-overrides.css"));

        var head = Head(await ShellAsync(options, assets, embedded));

        var theme = IndexOf(head, "href=\"/_netgrid/css/netopengrid-grid.css");
        var brand = IndexOf(head, "<style>:root{--ng-brand:#0d9488}</style>");
        var global = IndexOf(head, "<link rel=\"stylesheet\" href=\"/css/site.css\">");
        var grid = IndexOf(head, "<link rel=\"stylesheet\" href=\"/css/grid-overrides.css\">");
        Assert.True(theme < brand && brand < global && global < grid, "expected theme < brand < global < grid");
    }

    [Fact]
    public async Task GridBrandColour_BeatsTheGlobalOne()
    {
        var assets = new NetOpenGridAssetOptions { BrandColor = "#4f46e5" };
        var options = Options(b => b.WithBrandColor("oklch(0.600 0.118 185)"));

        var head = Head(await ShellAsync(options, assets));

        Assert.Contains("<style>:root{--ng-brand:oklch(0.600 0.118 185)}</style>", head);
        Assert.DoesNotContain("#4f46e5", head);
    }

    [Fact]
    public async Task StylesheetHref_IsHtmlEncoded()
    {
        var head = Head(await ShellAsync(Options(b => b.WithStylesheet("/css/a.css?v=1&t=dark")), new NetOpenGridAssetOptions()));

        Assert.Contains("href=\"/css/a.css?v=1&amp;t=dark\"", head);
    }

    [Fact]
    public async Task HandBuiltOptions_WithUnsafeValues_AreDroppedAtRender()
    {
        // GridOptions' init setters do not validate; the renderer re-checks before writing.
        var options = new GridOptions<Row>
        {
            Id = "brand",
            Columns = Options().Columns,
            BrandColor = "red}</style><script>alert(1)</script>",
            Stylesheets = ["javascript:alert(1)", "/css/ok.css"],
        };

        var head = Head(await ShellAsync(options, new NetOpenGridAssetOptions()));

        Assert.DoesNotContain("--ng-brand", head);
        Assert.DoesNotContain("alert(1)", head);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/css/ok.css\">", head);
    }

    [Fact]
    public void AssetOptions_ValidateAsTheyAreSet()
    {
        var assets = new NetOpenGridAssetOptions();

        Assert.Throws<GridConfigurationException>(() => assets.BrandColor = "red;}body{display:none");
        Assert.Throws<GridConfigurationException>(() => assets.ExtraStylesheets.Add("javascript:alert(1)"));
        Assert.Throws<GridConfigurationException>(() => assets.ExtraStylesheets.Add("data:text/css,*{}"));

        assets.BrandColor = "  ";
        Assert.Null(assets.BrandColor);
        assets.BrandColor = " teal ";
        Assert.Equal("teal", assets.BrandColor);
        assets.ExtraStylesheets.Add("https://cdn.example.com/brand.css");
        Assert.Equal(["https://cdn.example.com/brand.css"], assets.ExtraStylesheets);
    }

    [Fact]
    public void HostPageHead_CarriesTheGlobalBrandAndStylesheetsAfterTheTheme()
    {
        var assets = new NetOpenGridAssetOptions { BrandColor = "#0d9488", ExtraStylesheets = { "/css/grid-overrides.css" } };

        var head = GridAssetTags.Head(assets, "grid");

        var theme = IndexOf(head, "netopengrid-grid.css");
        var brand = IndexOf(head, "<style>:root{--ng-brand:#0d9488}</style>");
        var link = IndexOf(head, "<link rel=\"stylesheet\" href=\"/css/grid-overrides.css\">");
        Assert.True(theme < brand && brand < link);
    }

    [Fact]
    public void EveryEmbeddedTheme_DefinesTheSemanticAndLegacyBadges()
    {
        foreach (var (name, asset) in EmbeddedGridAssets.Themes)
        {
            var css = Encoding.UTF8.GetString(asset.Bytes);
            foreach (var badge in SemanticBadges.Concat(LegacyBadges))
            {
                // Selector, possibly grouped with its alias: ".badge-x{", ".badge-x," or inside :is(...).
                Assert.Matches(new Regex($@"\.{badge}[{{,)]"), css);
            }

            foreach (var variant in SemanticBadges.Skip(1))
            {
                // Each variant has a dark-mode rule too.
                Assert.True(
                    Regex.IsMatch(css, $@"\.{variant}[,)][^{{}}]*:where\(\.dark") || css.Contains($".{variant}:where(.dark", StringComparison.Ordinal),
                    $"theme '{name}' has no dark rule for .{variant}");
            }
        }
    }

    [Fact]
    public void EveryEmbeddedTheme_DerivesItsBrandScaleFromNgBrand()
    {
        foreach (var (name, asset) in EmbeddedGridAssets.Themes)
        {
            var css = Encoding.UTF8.GetString(asset.Bytes);
            foreach (var step in new[] { 50, 100, 200, 300, 400, 500, 600, 700, 800, 900 })
            {
                // The scale utilities read falls back to the theme's own hex while --ng-brand is unset.
                Assert.Matches(new Regex($@"--color-brand-{step}:var\(--ng-brand-{step},#[0-9a-f]{{3,8}}\)"), css);
                Assert.Contains($"--ng-brand-{step}:", css);
            }

            Assert.Contains("--ng-brand-600:var(--ng-brand)", css);
            Assert.Contains("color-mix(in oklch, var(--ng-brand) 5%, white)", css);
            Assert.Contains("color-mix(in oklch, var(--ng-brand) 64%, black)", css);
            // Utilities keep pointing at the variable, so the runtime value takes effect.
            Assert.True(css.Contains("background-color:var(--color-brand-600)", StringComparison.Ordinal), $"theme '{name}'");
        }
    }
}
