using NetOpenGrid.Application.Builders;
using Xunit;

namespace NetOpenGrid.Application.Tests;

/// <summary>
/// A column declared from a member ("BirthDate") and one declared by hand ("birthDate") must read
/// the same. Before, only the first arrived capitalized.
/// </summary>
public class ColumnNameHumanizerTests
{
    private static string Header(string field) =>
        new GridOptionsBuilder<Person>()
            .WithId("people")
            .AddColumn(field, p => p.Name)
            .Build()
            .Columns
            .Single()
            .Header;

    [Theory]
    [InlineData("email", "Email")]
    [InlineData("id", "Id")]
    [InlineData("birthDate", "Birth date")]
    [InlineData("BirthDate", "Birth date")]
    [InlineData("inventoryValue", "Inventory value")]
    public void Hand_written_fields_are_sentence_cased(string field, string expected) =>
        Assert.Equal(expected, Header(field));

    [Fact]
    public void A_member_name_and_the_same_name_written_by_hand_read_the_same()
    {
        var fromMember = new GridOptionsBuilder<Person>()
            .WithId("a")
            .AddColumn(p => p.BirthDate)
            .Build()
            .Columns
            .Single()
            .Header;

        Assert.Equal(fromMember, Header("birthDate"));
    }

    [Fact]
    public void An_all_caps_acronym_is_left_alone()
    {
        // Sentence case only touches a lowercase first letter; it never lowers a word the caller
        // already wrote in capitals.
        Assert.Equal("URL", Header("URL"));
    }
}
