using NetOpenGrid.Application.Builders;
using Xunit;

namespace NetOpenGrid.Application.Tests;

/// <summary>
/// A column that only carries a row action has nothing to title, so its heading may be blank.
/// What must never be blank is the name the column answers to in a menu, an aria-label or an
/// export: an empty option cannot be picked and an empty aria-label tells a screen reader nothing.
/// </summary>
public class ColumnHeaderTests
{
    private static Domain.Columns.GridColumn<Person> Column(Action<GridColumnBuilder<Person, string>> configure) =>
        new GridOptionsBuilder<Person>()
            .WithId("people")
            .AddColumn("actions", p => p.Name, configure)
            .Build()
            .Columns
            .Single();

    [Fact]
    public void An_empty_header_is_accepted_and_kept_empty()
    {
        var column = Column(c => c.Header(string.Empty));

        Assert.Equal(string.Empty, column.Header);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_header_the_label_falls_back_to_the_field_name(string header)
    {
        var column = Column(c => c.Header(header));

        Assert.Equal("actions", column.Label);
    }

    [Fact]
    public void With_a_header_the_label_is_the_header()
    {
        var column = Column(c => c.Header("Detail"));

        Assert.Equal("Detail", column.Header);
        Assert.Equal("Detail", column.Label);
    }

    [Fact]
    public void An_expression_column_with_a_blank_header_still_has_a_readable_label()
    {
        // The case a row-action column actually hits: the heading is blank on purpose, but the
        // member name is known, so menus and aria-labels use it instead of falling to the field.
        var column = new GridOptionsBuilder<Person>()
            .WithId("people")
            .AddColumn(p => p.BirthDate, c => c.Header(string.Empty))
            .Build()
            .Columns
            .Single();

        Assert.Equal(string.Empty, column.Header);
        Assert.Equal("Birth date", column.Label);
    }

    [Fact]
    public void A_null_header_is_still_rejected()
    {
        // Null is a caller mistake; empty is a deliberate choice.
        Assert.Throws<ArgumentNullException>(() => Column(c => c.Header(null!)));
    }

    [Fact]
    public void A_column_that_was_never_given_a_header_keeps_the_humanized_one()
    {
        var column = Column(_ => { });

        Assert.Equal("actions", column.Header);
        Assert.Equal("actions", column.Label);
    }
}
