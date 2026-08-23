using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Columns;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public sealed record Person(int Id, string Name, string City, decimal Salary, DateOnly BirthDate);

public static class TestGrid
{
    public static GridOptions<Person> Options(Action<GridOptionsBuilder<Person>>? extra = null)
    {
        var builder = new GridOptionsBuilder<Person>()
            .WithId("people")
            .AddColumn("id", p => p.Id, c => c.Header("Id").Filterable(false))
            .AddColumn("name", p => p.Name, c => c.Header("Name").Searchable())
            .AddColumn("city", p => p.City, c => c.Header("City"))
            .AddColumn("salary", p => p.Salary, c => c.Header("Salary"))
            .AddColumn("birthDate", p => p.BirthDate, c => c.Header("Birth date"));

        extra?.Invoke(builder);
        return builder.Build();
    }

    public static readonly IReadOnlyList<Person> People =
    [
        new(1, "Ana Torres", "Madrid", 55_000m, new DateOnly(1990, 5, 12)),
        new(2, "Bruno Diaz", "Lima", 72_500m, new DateOnly(1985, 11, 2)),
        new(3, "Carla Gomez", "Bogota", 48_000m, new DateOnly(1992, 1, 30)),
        new(4, "Diego Ruiz", "Lima", 90_000m, new DateOnly(1980, 7, 21)),
        new(5, "Elena Paz", null!, 61_000m, new DateOnly(1995, 3, 8))
    ];
}

public class GridOptionsBuilderTests
{
    [Fact]
    public void Build_WithoutId_Throws() =>
        Assert.Throws<GridConfigurationException>(() =>
            new GridOptionsBuilder<Person>().AddColumn("name", p => p.Name).Build());

    [Fact]
    public void Build_WithDuplicateField_Throws() =>
        Assert.Throws<GridConfigurationException>(() =>
            new GridOptionsBuilder<Person>()
                .WithId("x")
                .AddColumn("name", p => p.Name)
                .AddColumn("name", p => p.City)
                .Build());

    [Fact]
    public void Build_DerivesFieldNameAndHumanHeaderFromExpression()
    {
        var options = new GridOptionsBuilder<Person>()
            .WithId("people")
            .AddColumn(p => p.BirthDate)
            .Build();

        var column = options.Columns.Single();
        Assert.Equal("birthDate", column.Field);
        Assert.Equal("Birth date", column.Header);
    }

    [Fact]
    public void EnableRowSelection_CapturesRowKey()
    {
        var options = TestGrid.Options(b => b.EnableRowSelection(p => p.Id.ToString()));

        Assert.True(options.EnableRowSelection);
        Assert.NotNull(options.RowKey);
    }

    [Fact]
    public void Build_SortsPageSizeChoices()
    {
        var options = TestGrid.Options(b => b.WithPageSizeChoices(50, 10, 25));

        Assert.Equal([10, 25, 50], options.PageSizeChoices);
    }

    [Fact]
    public void MinHeight_DefaultsToApproximately25Rows() =>
        Assert.Equal("64rem", TestGrid.Options().MinHeight);

    [Fact]
    public void WithMinHeight_OverridesDefault_AndEmptyDisablesIt()
    {
        var custom = TestGrid.Options(b => b.WithMinHeight("32rem"));
        var disabled = TestGrid.Options(b => b.WithMinHeight(""));

        Assert.Equal("32rem", custom.MinHeight);
        Assert.Equal(string.Empty, disabled.MinHeight);
    }
}
