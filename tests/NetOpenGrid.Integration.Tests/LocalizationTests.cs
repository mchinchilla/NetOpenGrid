using Microsoft.Extensions.DependencyInjection;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Runtime;
using NetOpenGrid.Infrastructure.Export;
using NetOpenGrid.Infrastructure.Rendering;
using System.Globalization;
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

    [Fact]
    public async Task FollowingTheRequestCulture_PicksThePresetPerRender()
    {
        var locale = new NetOpenGridLocalizationOptions { FollowCurrentUICulture = true }
            .Set("filter.apply", "Go");
        var renderer = Renderer(locale);
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-HN");
            var spanish = await renderer.RenderShellAsync(EmptyResult());
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var english = await renderer.RenderShellAsync(EmptyResult());
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var unknown = await renderer.RenderShellAsync(EmptyResult());

            Assert.Contains("<html lang=\"es\">", spanish);
            Assert.Contains("Anterior", spanish);
            Assert.Contains("\"ops.contains\":\"contiene\"", spanish);
            Assert.Contains(">Go</button>", spanish);

            Assert.Contains("<html lang=\"en\">", english);
            Assert.Contains(">Prev</button>", english);
            Assert.Contains(">Go</button>", english);

            // Sin preset para el idioma, se queda con lo configurado (inglés + overrides).
            Assert.Contains("<html lang=\"en\">", unknown);
            Assert.Contains(">Prev</button>", unknown);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public async Task TextLocalizer_TranslatesHeadersTitleAndCsvHeader()
    {
        var options = new GridOptionsBuilder<LocaleRow>()
            .WithId("loc")
            .WithTitle("People")
            .AddColumn("name", r => r.Name, c => c.Header("Name"))
            .AddColumn("city", r => r.City, c => c.Header("City"))
            .Build();
        var locale = new NetOpenGridLocalizationOptions
        {
            TextLocalizer = text => text switch { "People" => "Personas", "Name" => "Nombre", "City" => "Ciudad", _ => text },
        };
        var renderer = new GridHtmlRenderer<LocaleRow>(options, new NetOpenGridAssetOptions(), locale);

        var html = await renderer.RenderShellAsync(EmptyResult());

        Assert.Contains("<title>Personas</title>", html);
        Assert.Contains(">Nombre<", html);
        Assert.Contains(">Ciudad<", html);
        Assert.Contains("\"header\":\"Nombre\"", html);
        Assert.DoesNotContain(">Name<", html);

        var csv = GridCsvExporter.Build(options.Columns, [new LocaleRow("Ana", "SPS")], locale.Text);
        Assert.StartsWith("Nombre,Ciudad\r\n", csv);
    }

    // Option texts configured as resource keys: every one of them goes through TextLocalizer.
    private static readonly Dictionary<string, string> Spanish = new(StringComparer.Ordinal)
    {
        ["grid.empty"] = "Sin registros",
        ["nav.home"] = "Inicio",
        ["action.open"] = "Abrir",
        ["action.disable"] = "Deshabilitar",
        ["actions.custom"] = "Operaciones",
        ["view.mine"] = "Mis clientes",
    };

    private static GridOptions<LocaleRow> KeyedOptions(bool customActionsHeader = true) =>
        new GridOptionsBuilder<LocaleRow>()
            .WithId("loc")
            .WithEmptyMessage("grid.empty")
            .WithNavLinks(new GridNavLink("nav.home", "/"))
            .WithRowKey(r => r.Name)
            .WithRowActions(a =>
            {
                a.Link("action.open", r => $"/people/{r.Name}").Event("disable", "action.disable");
                if (customActionsHeader)
                {
                    a.Header("actions.custom");
                }
            })
            .AddView("view.mine", "sort=name:asc")
            .AddColumn("name", r => r.Name)
            .AddColumn("city", r => r.City)
            .Build();

    private static NetOpenGridLocalizationOptions SpanishTexts() =>
        new() { TextLocalizer = text => Spanish.GetValueOrDefault(text, text) };

    private static GridExecutionResult<LocaleRow> OneRow() =>
        new(GridQuery.Empty, new PageResult<LocaleRow>([new LocaleRow("Ana", "SPS")], 1, 1, 10));

    [Fact]
    public async Task TextLocalizer_TranslatesRowActionLabelsHeaderNavLinksAndViewNames()
    {
        var renderer = new GridHtmlRenderer<LocaleRow>(KeyedOptions(), new NetOpenGridAssetOptions(), SpanishTexts());

        var html = await renderer.RenderShellAsync(OneRow());

        Assert.Contains(">Abrir</a>", html);
        Assert.Contains(">Deshabilitar</button>", html);
        Assert.Contains(">Operaciones<", html);
        Assert.Contains(">Inicio</a>", html);
        Assert.Contains("{\"name\":\"Mis clientes\",\"query\":\"sort=name:asc\"}", html);
        foreach (var key in new[] { "action.open", "action.disable", "actions.custom", "nav.home", "view.mine" })
        {
            Assert.DoesNotContain(key, html);
        }
    }

    [Fact]
    public async Task TextLocalizer_TranslatesTheEmptyMessage_InPlainAndGroupedBodies()
    {
        var renderer = new GridHtmlRenderer<LocaleRow>(KeyedOptions(), new NetOpenGridAssetOptions(), SpanishTexts());

        var rows = await renderer.RenderRowsAsync(EmptyResult());
        var grouped = await renderer.RenderGroupedRowsAsync(new GroupedPageResult<LocaleRow>([], 0, 1, 10));

        Assert.Contains(">Sin registros</td>", rows);
        Assert.Contains(">Sin registros</td>", grouped);
        Assert.DoesNotContain("grid.empty", rows + grouped);
    }

    [Fact]
    public async Task TextLocalizer_LeavesTheBuiltInActionsHeaderToTheLabelTable()
    {
        // No Header(...): the default comes from L("actions.header"), never through TextLocalizer.
        var locale = new NetOpenGridLocalizationOptions { TextLocalizer = text => "X" + text };
        var renderer = new GridHtmlRenderer<LocaleRow>(KeyedOptions(customActionsHeader: false), new NetOpenGridAssetOptions(), locale);

        var html = await renderer.RenderShellAsync(OneRow());

        Assert.Contains(">" + locale["actions.header"] + "<", html);
        Assert.DoesNotContain("X" + locale["actions.header"], html);
    }

    [Fact]
    public async Task IdentityTextLocalizer_RendersExactlyWhatNoLocalizerRenders()
    {
        var plain = new GridHtmlRenderer<LocaleRow>(KeyedOptions(), new NetOpenGridAssetOptions(), new NetOpenGridLocalizationOptions());
        var identity = new GridHtmlRenderer<LocaleRow>(
            KeyedOptions(), new NetOpenGridAssetOptions(), new NetOpenGridLocalizationOptions { TextLocalizer = text => text });

        Assert.Equal(await plain.RenderShellAsync(OneRow()), await identity.RenderShellAsync(OneRow()));
        Assert.Equal(await plain.RenderRowsAsync(EmptyResult()), await identity.RenderRowsAsync(EmptyResult()));
    }

    [Fact]
    public async Task Pager_shows_more_than_cap()
    {
        var capped = new GridExecutionResult<LocaleRow>(
            GridQuery.Empty,
            new PageResult<LocaleRow>([.. Enumerable.Repeat(new LocaleRow("A", "B"), 10)], 10001, 1, 10) { TotalCountIsCapped = true });

        var es = await Renderer(new NetOpenGridLocalizationOptions().UseCulture("es")).RenderShellAsync(capped);
        var en = await Renderer(new NetOpenGridLocalizationOptions()).RenderShellAsync(capped);

        Assert.Contains("\"capped\":true", es);
        Assert.Contains("\"range.formatCapped\":", es);
        Assert.Contains("\"records.manyCapped\":", es);
        var esLocale = new NetOpenGridLocalizationOptions().UseCulture("es");
        Assert.Equal("{from}-{to} de más de {total}", esLocale["range.formatCapped"]);
        Assert.Equal("más de {n} registros", esLocale["records.manyCapped"]);
        Assert.Equal("{from}-{to} of more than {total}", new NetOpenGridLocalizationOptions()["range.formatCapped"]);
        Assert.Contains("\"range.formatCapped\":", en);

        // "Next" stays available while the page is full, even past the capped total.
        var page = new PageResult<LocaleRow>([.. Enumerable.Repeat(new LocaleRow("A", "B"), 10)], 10001, 1001, 10) { TotalCountIsCapped = true };
        Assert.True(page.HasNext);

        // Uncapped output is untouched.
        Assert.DoesNotContain("capped\":true", await Renderer(new NetOpenGridLocalizationOptions()).RenderShellAsync(EmptyResult()));
    }
}
