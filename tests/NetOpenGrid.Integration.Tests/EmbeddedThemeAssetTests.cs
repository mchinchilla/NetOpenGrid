using NetOpenGrid.Infrastructure.Assets;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class EmbeddedThemeAssetTests
{
    [Fact]
    public void Themes_ContainsEveryCompiledTailwindTheme()
    {
        Assert.True(EmbeddedGridAssets.Themes.ContainsKey("grid"), "theme 'grid' is not embedded in the assembly");
        Assert.True(EmbeddedGridAssets.Themes.ContainsKey("midnight"), "theme 'midnight' is not embedded in the assembly");
        Assert.True(EmbeddedGridAssets.Themes.ContainsKey("tekium"), "theme 'tekium' is not embedded in the assembly");
    }

    [Fact]
    public void Themes_AreNonEmptyCssWithStableVersions()
    {
        Assert.NotEmpty(EmbeddedGridAssets.Themes);

        foreach (var (name, asset) in EmbeddedGridAssets.Themes)
        {
            Assert.NotEmpty(asset.Bytes);
            Assert.Equal("text/css; charset=utf-8", asset.ContentType);
            Assert.Equal(12, asset.Version.Length);
            Assert.DoesNotContain('.', name);
        }
    }

    [Fact]
    public void Themes_LookupIsCaseInsensitive()
    {
        Assert.True(EmbeddedGridAssets.Themes.ContainsKey("GRID"));
    }
}
