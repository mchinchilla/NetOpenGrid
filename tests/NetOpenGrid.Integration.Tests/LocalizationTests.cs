using Microsoft.Extensions.DependencyInjection;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Runtime;
using NetOpenGrid.Infrastructure.Rendering;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public sealed record LocaleRow(string Name, string City);

public class LocalizationTests
{
    private static GridOptions<LocaleRow> Options() =>
        new GridOptionsBuilder<LocaleRow>()
            .WithId("loc")
            .AddColumn("name", r => r.Name, c => c.Searchable())
            .AddColumn("city", r => r.City)
            .Build();

    private static GridHtmlRenderer<LocaleRow> Renderer(NetOpenGridLocalizationOptions locale) =>
        new(Options(), new NetOpenGridAssetOptions(), locale);

    private static GridExecutionResult<LocaleRow> EmptyResult() =>
        new(GridQuery.Empty, new PageResult<LocaleRow>([], 0, 1, 10));

    [Fact]
    public void Defaults_AreEnglish()
    {
        var locale = new NetOpenGridLocalizationOptions();

        Assert.Equal("Prev", locale["pager.prev"]);
        Assert.Equal("Next", locale["pager.next"]);
        Assert.Equal("Apply", locale["filter.apply"]);
        Assert.Equal("contains", locale["ops.contains"]);
    }

    [Fact]
    public void UseCulture_AppliesSpanishPreset_AndSetOverrides()
    {
        var locale = new NetOpenGridLocalizationOptions()
            .UseCulture("es")
            .Set("filter.apply", "Filtrar");

        Assert.Equal("Anterior", locale["pager.prev"]);
        Assert.Equal("contiene", locale["ops.contains"]);
        Assert.Equal("Filtrar", locale["filter.apply"]);
        Assert.Equal("Buscar...", locale["search.placeholder"]);
    }

    [Fact]
    public async Task SpanishShell_RendersLocalizedStrings_AndLocaleBlob()
    {
        var locale = new NetOpenGridLocalizationOptions().UseCulture("es");
        var html = await Renderer(locale).RenderShellAsync(EmptyResult());

        Assert.Contains("Anterior", html);
        Assert.Contains("Siguiente", html);
        Assert.Contains("Aplicar", html);
        Assert.Contains("Seleccionar todo", html);
        Assert.Contains("Buscar...", html);
        Assert.Contains("__NETGRID__.locale=", html);
        Assert.Contains("\"ops.contains\":\"contiene\"", html);
    }

    [Fact]
    public async Task EnglishShell_RendersDefaults()
    {
        var html = await Renderer(new NetOpenGridLocalizationOptions()).RenderShellAsync(EmptyResult());

        Assert.Contains(">Prev</button>", html);
        Assert.Contains(">Apply</button>", html);
        Assert.Contains("Select all", html);
    }

    [Fact]
    public void Di_RegistersLocalizationSingleton()
    {
        var services = new ServiceCollection();
        services.AddNetOpenGrid(_ => { }, loc => loc.UseCulture("es"));

        var locale = services.BuildServiceProvider().GetRequiredService<NetOpenGridLocalizationOptions>();

        Assert.Equal("Anterior", locale["pager.prev"]);
    }
}
