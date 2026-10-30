using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;
using NetOpenGrid.Infrastructure.Runtime;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class CappedCountExportTests
{
    private sealed record Item(string Name);

    // Mimics a Npgsql source with CountCap: interactive loads report cap + 1, ExactCount loads the real total.
    private sealed class CappedSource(int rows, int cap) : IGridDataSource<Item>
    {
        public ValueTask<PageResult<Item>> LoadAsync(GridQuery query, CancellationToken cancellationToken = default)
        {
            var size = query.Paging.PageSize;
            var items = Enumerable.Range(0, rows).Skip((query.Paging.Page - 1) * size).Take(size)
                .Select(i => new Item($"row{i}")).ToArray();
            var capped = !query.ExactCount && rows > cap;
            return ValueTask.FromResult(new PageResult<Item>(items, capped ? cap + 1 : rows, query.Paging.Page, size)
            {
                TotalCountIsCapped = capped
            });
        }
    }

    private static GridRuntime<Item> Runtime(int rows, int cap, int maxExportRows)
    {
        var options = new GridOptionsBuilder<Item>()
            .WithId("cap")
            .AddColumn("name", r => r.Name)
            .WithMaxPageSize(100)
            .WithMaxExportRows(maxExportRows)
            .Build();
        return new GridRuntime<Item>(options, new CappedSource(rows, cap), new NetOpenGridAssetOptions(), new NetOpenGridLocalizationOptions());
    }

    [Fact]
    public async Task Export_of_a_capped_map_writes_all_rows_beyond_the_cap()
    {
        var export = await Runtime(rows: 120, cap: 50, maxExportRows: 1000)
            .CreateExportAsync(GridRequestValues.Empty, GridExportFormats.Csv);

        Assert.Equal(120, export.TotalCount);
        Assert.False(export.ExceedsLimit);

        using var ms = new MemoryStream();
        await export.WriteToAsync(ms);
        var lines = System.Text.Encoding.UTF8.GetString(ms.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(121, lines.Length);   // header + 120 rows
    }

    [Fact]
    public async Task ExceedsLimit_uses_the_exact_total_even_when_cap_is_below_max_rows()
    {
        // cap (50) < MaxRows (100) < real total (120): a capped total (51) would wrongly pass.
        var export = await Runtime(rows: 120, cap: 50, maxExportRows: 100)
            .CreateExportAsync(GridRequestValues.Empty, GridExportFormats.Csv);

        Assert.Equal(120, export.TotalCount);
        Assert.True(export.ExceedsLimit);
    }

    [Fact]
    public async Task Interactive_page_load_still_uses_the_cap()
    {
        var response = await Runtime(rows: 120, cap: 50, maxExportRows: 1000).RenderRowsAsync(GridRequestValues.Empty);

        Assert.Equal(51, response.TotalCount);
        Assert.True(response.TotalCountIsCapped);
    }

    [Fact]
    public async Task Virtual_scroll_grid_never_uses_the_cap()
    {
        var options = new GridOptionsBuilder<Item>()
            .WithId("capv")
            .AddColumn("name", r => r.Name)
            .WithVirtualScroll(blockSize: 50)
            .Build();
        var runtime = new GridRuntime<Item>(options, new CappedSource(120, 50), new NetOpenGridAssetOptions(), new NetOpenGridLocalizationOptions());

        var response = await runtime.RenderRowsAsync(GridRequestValues.Empty);

        Assert.Equal(120, response.TotalCount);
        Assert.False(response.TotalCountIsCapped);
    }
}
