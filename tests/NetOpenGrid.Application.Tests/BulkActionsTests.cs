using System.Text.Json;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Json;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public class BulkActionsTests
{
    private sealed record Entry(string Id, string Status);

    private static GridOptionsBuilder<Entry> Builder() => new GridOptionsBuilder<Entry>()
        .WithId("entries")
        .AddColumn("id", e => e.Id);

    [Fact]
    public void Bulk_Actions_Keep_Their_Order_And_Settings()
    {
        var options = Builder()
            .EnableRowSelection(e => e.Id)
            .AddBulkAction("post", a => a
                .Label("Post selected")
                .Icon(" fas fa-check-double ")
                .Tone(BulkActionTone.Success)
                .Confirm()
                .When(e => e.Status == "draft"))
            .AddBulkAction("approve")
            .Build();

        Assert.Equal(["post", "approve"], options.BulkActions.Select(a => a.Name));

        var post = options.BulkActions[0];
        Assert.Equal("Post selected", post.Label);
        Assert.Equal("fas fa-check-double", post.Icon);
        Assert.Equal(BulkActionTone.Success, post.Tone);
        Assert.True(post.Confirm);
        Assert.True(post.When!(new Entry("1", "draft")));
        Assert.False(post.When!(new Entry("2", "posted")));

        // Defaults: the name as label, no icon, primary, no confirm (the host confirms), every row.
        var approve = options.BulkActions[1];
        Assert.Equal("approve", approve.Label);
        Assert.Null(approve.Icon);
        Assert.Equal(BulkActionTone.Primary, approve.Tone);
        Assert.False(approve.Confirm);
        Assert.Null(approve.When);
    }

    [Fact]
    public void Row_Selectable_Is_Carried_To_The_Options()
    {
        var options = Builder()
            .EnableRowSelection(e => e.Id)
            .RowSelectable(e => e.Status != "void")
            .Build();

        Assert.True(options.RowSelectable!(new Entry("1", "draft")));
        Assert.False(options.RowSelectable!(new Entry("2", "void")));
    }

    [Fact]
    public void Selection_Scope_Can_Be_Limited_To_The_Page()
    {
        var typed = Builder().EnableRowSelection(e => e.Id).WithSelectionScope(GridSelectionScope.Page).Build();
        var json = new JsonGridOptionsBuilder()
            .WithId("entries")
            .AddColumn("id")
            .EnableRowSelection(e => e.GetProperty("id").GetString())
            .WithSelectionScope(GridSelectionScope.Page)
            .Build();

        Assert.Equal(GridSelectionScope.Page, typed.SelectionScope);
        Assert.Equal(GridSelectionScope.Page, json.SelectionScope);
    }

    [Fact]
    public void Without_Bulk_Configuration_Nothing_Changes()
    {
        var options = Builder().EnableRowSelection(e => e.Id).Build();

        // The selection keeps surviving paging, sorting and filtering, as it did before 1.4.3.
        Assert.Equal(GridSelectionScope.All, options.SelectionScope);

        Assert.Empty(options.BulkActions);
        Assert.Null(options.RowSelectable);
    }

    [Fact]
    public void Bulk_Actions_Need_Row_Selection()
    {
        var error = Assert.Throws<GridConfigurationException>(() => Builder()
            .WithRowKey(e => e.Id)
            .AddBulkAction("post")
            .Build());

        Assert.Contains("EnableRowSelection", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Row_Selectable_Needs_Row_Selection()
    {
        var error = Assert.Throws<GridConfigurationException>(() => Builder()
            .RowSelectable(e => e.Status == "draft")
            .Build());

        Assert.Contains("EnableRowSelection", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_Bulk_Actions_Are_Rejected()
    {
        var error = Assert.Throws<GridConfigurationException>(() => Builder()
            .EnableRowSelection(e => e.Id)
            .AddBulkAction("post")
            .AddBulkAction("post"));

        Assert.Contains("Duplicate bulk action 'post'", error.Message, StringComparison.Ordinal);
    }

    // The name is written into data-bulk (space separated) and into a JS string literal.
    [Theory]
    [InlineData("post entries")]
    [InlineData("post'")]
    [InlineData("1post")]
    [InlineData("post\"")]
    public void Bulk_Action_Names_Are_Restricted(string name)
    {
        Assert.Throws<GridConfigurationException>(() => Builder().AddBulkAction(name));
    }

    [Fact]
    public void Json_Builder_Offers_The_Same_Api()
    {
        var options = new JsonGridOptionsBuilder()
            .WithId("entries")
            .AddColumn("id")
            .EnableRowSelection(e => e.GetProperty("id").GetString())
            .RowSelectable(e => e.GetProperty("status").GetString() != "void")
            .AddBulkAction("post", a => a.Label("Post").When(e => e.GetProperty("status").GetString() == "draft"))
            .Build();

        using var document = JsonDocument.Parse("""{"id":"1","status":"draft"}""");
        Assert.Single(options.BulkActions);
        Assert.True(options.BulkActions[0].When!(document.RootElement));
        Assert.True(options.RowSelectable!(document.RootElement));

        var error = Assert.Throws<GridConfigurationException>(() => new JsonGridOptionsBuilder()
            .WithId("entries")
            .AddColumn("id")
            .AddBulkAction("post")
            .Build());
        Assert.Contains("EnableRowSelection", error.Message, StringComparison.Ordinal);
    }
}
