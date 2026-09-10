using NetOpenGrid.Application.Builders;
using NetOpenGrid.Domain.Columns;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public sealed record Product(string Sku, string Name);

public class HideBelowBuilderTests
{
    [Fact]
    public void Builder_Records_The_Breakpoint()
    {
        var options = new GridOptionsBuilder<Product>()
            .WithId("p")
            .AddColumn("sku", p => p.Sku, c => c.HideBelow(ResponsiveBreakpoint.Lg))
            .Build();

        Assert.True(options.TryGetColumn("sku", out var column));
        Assert.Equal(ResponsiveBreakpoint.Lg, column.HideBelow);
    }

    [Fact]
    public void Default_Is_None()
    {
        var options = new GridOptionsBuilder<Product>()
            .WithId("p")
            .AddColumn("sku", p => p.Sku, c => c.Header("SKU"))
            .Build();

        Assert.True(options.TryGetColumn("sku", out var column));
        Assert.Equal(ResponsiveBreakpoint.None, column.HideBelow);
    }
}
