using System.Text;
using System.Text.RegularExpressions;
using NetOpenGrid.Infrastructure.Assets;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>
/// The theme's component classes only apply inside a grid (<c>[data-netgrid]</c> on its root): a host page that compiles the
/// theme into its own stylesheet keeps its own <c>.btn-ghost</c>, <c>.badge</c> or <c>.chip</c>. Utilities (<c>.px-4</c>) stay
/// global: they are the same Tailwind output a host would generate.
/// </summary>
public partial class ThemeScopeTests
{
    private static readonly string[] ComponentClasses =
        ["input-base", "btn-icon", "btn-ghost", "btn-ghost-sm", "btn-primary-sm", "chip", "chip-invalid", "badge", "badge-success", "badge-brand", "btn-bulk", "btn-bulk-danger"];

    public static TheoryData<string> Themes => [.. EmbeddedGridAssets.Themes.Keys];

    [Theory]
    [MemberData(nameof(Themes))]
    public void Component_Classes_Are_Scoped_To_The_Grid(string theme)
    {
        var css = Encoding.UTF8.GetString(EmbeddedGridAssets.Themes[theme].Bytes);

        foreach (var name in ComponentClasses)
        {
            foreach (Match rule in Selector(name).Matches(css))
            {
                Assert.Contains("[data-netgrid]", rule.Groups["selector"].Value, StringComparison.Ordinal);
            }

            Assert.Matches($@"\[data-netgrid\][^{{]*\.{Regex.Escape(name)}(?![\w-])", css);
        }
    }

    [Fact]
    public async Task Grid_Root_Carries_The_Scope_Attribute()
    {
        var html = await new Infrastructure.Rendering.GridHtmlRenderer<string>(
                new Application.Builders.GridOptionsBuilder<string>().WithId("plain").AddColumn("value", v => v).Build(),
                new Infrastructure.Runtime.NetOpenGridAssetOptions())
            .RenderFragmentAsync(new Application.Engine.GridExecutionResult<string>(
                Domain.GridQuerying.GridQuery.Empty, new Domain.Results.PageResult<string>(["a"], 1, 1, 10)));

        Assert.Contains("x-data=\"netgrid('plain')\" data-netgrid ", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Theme_Toggle_Is_On_By_Default_And_Can_Be_Turned_Off()
    {
        static Task<string> Render(bool toggle) => new Infrastructure.Rendering.GridHtmlRenderer<string>(
                new Application.Builders.GridOptionsBuilder<string>().WithId("plain").WithThemeToggle(toggle).AddColumn("value", v => v).Build(),
                new Infrastructure.Runtime.NetOpenGridAssetOptions())
            .RenderFragmentAsync(new Application.Engine.GridExecutionResult<string>(
                Domain.GridQuerying.GridQuery.Empty, new Domain.Results.PageResult<string>(["a"], 1, 1, 10))).AsTask();

        Assert.Contains("toggleTheme()", await Render(true), StringComparison.Ordinal);
        Assert.DoesNotContain("toggleTheme()", await Render(false), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Title_Heading_Level_Is_Configurable()
    {
        static Task<string> Render(Application.Builders.GridOptionsBuilder<string> builder) => new Infrastructure.Rendering.GridHtmlRenderer<string>(
                builder.WithId("plain").WithTitle("Plans").AddColumn("value", v => v).Build(), new Infrastructure.Runtime.NetOpenGridAssetOptions())
            .RenderFragmentAsync(new Application.Engine.GridExecutionResult<string>(
                Domain.GridQuerying.GridQuery.Empty, new Domain.Results.PageResult<string>(["a"], 1, 1, 10))).AsTask();

        Assert.Contains(">Plans</h1>", await Render(new()), StringComparison.Ordinal);
        var embedded = await Render(new Application.Builders.GridOptionsBuilder<string>().WithHeadingLevel(2));
        Assert.Contains(">Plans</h2>", embedded, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1", embedded, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Application.Builders.GridOptionsBuilder<string>().WithHeadingLevel(7));
    }

    /// <summary>Each complete selector (between braces or commas) that names the class.</summary>
    private static Regex Selector(string name) => new($@"(?:^|[{{}},])(?<selector>[^{{}},]*\.{Regex.Escape(name)}(?![\w-])[^{{}},]*)(?=[{{,])");
}
