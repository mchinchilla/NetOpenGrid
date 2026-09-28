using System.Text;
using NetOpenGrid.Infrastructure.Assets;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>
/// The client runtime rebuilds the query string from grid state alone, which drops anything the
/// host page put in the URL. <c>withHostParams</c> re-attaches those parameters, but ONLY where
/// the address bar is written.
///
/// <para><b>What these tests are.</b> Assertions over the shipped
/// <see cref="EmbeddedGridAssets.ClientRuntime"/> source. There is no JavaScript runtime in this
/// suite, so they cannot prove the merge behaves correctly at run time — a browser would be
/// needed for that. What they do pin is the part a later edit is most likely to break by
/// accident: <b>where</b> the merge is applied. That placement is the whole safety argument, so
/// it is worth a test even in this weaker form.</para>
/// </summary>
public class HostParamPreservationTests
{
    private static string ClientRuntime =>
        Encoding.UTF8.GetString(EmbeddedGridAssets.ClientRuntime.Bytes);

    [Fact]
    public void The_Client_Runtime_Carries_Host_Parameters_Through()
    {
        var js = ClientRuntime;

        Assert.Contains("function withHostParams(params)", js, StringComparison.Ordinal);
        Assert.Contains("const GRID_PARAMS = new Set(", js, StringComparison.Ordinal);
    }

    /// <summary>
    /// The merge must not reach <c>toParams</c> itself. That function also builds the
    /// <c>/rows</c> and <c>/values</c> fetches, the export URL and the query persisted in a
    /// saved view: merging there would send the host page's parameters to the server on every
    /// request and bake whatever was in the URL into stored state. For TekiumERP that is not
    /// hypothetical — its input-sanitization middleware answers 400 to any query value holding
    /// <c>--</c> or a SQL keyword, and htmx does not swap a 4xx, so the grid would simply stop
    /// responding with no message anywhere.
    ///
    /// <para>Exactly two occurrences: the declaration and the single call site.</para>
    /// </summary>
    [Fact]
    public void The_Merge_Is_Applied_Only_Where_The_Address_Bar_Is_Written()
    {
        var js = ClientRuntime;

        // Counted as applications, not as mentions of the name — the surrounding comments
        // reference it too, and a test that breaks when a comment is reworded is noise.
        var applications = js.Split("withHostParams(toParams").Length - 1;
        Assert.Equal(1, applications);

        Assert.Contains("withHostParams(toParams(state))", js, StringComparison.Ordinal);

        // Every other caller passes `this` (the Alpine component): the /rows and /values
        // fetches, the export URL and the saved-view query. None of them may be wrapped.
        Assert.DoesNotContain("withHostParams(toParams(this", js, StringComparison.Ordinal);

        // The call site and history.replaceState are the same statement pair, so the merge
        // cannot have drifted onto a fetch without this failing.
        var mergeAt = js.IndexOf("withHostParams(toParams(state))", StringComparison.Ordinal);
        var replaceStateAt = js.IndexOf("window.history.replaceState", mergeAt, StringComparison.Ordinal);
        Assert.InRange(replaceStateAt - mergeAt, 0, 200);
    }

    /// <summary>
    /// Every parameter <c>GridRequestParser</c> reads must be excluded from the merge, or the
    /// grid's own state would be appended a second time on each interaction and the query string
    /// would grow without bound.
    /// </summary>
    [Theory]
    [InlineData("'page'")]
    [InlineData("'pageSize'")]
    [InlineData("'sort'")]
    [InlineData("'filter'")]
    [InlineData("'q'")]
    [InlineData("'groupby'")]
    [InlineData("'expand'")]
    public void Grid_Owned_Parameters_Are_Excluded_From_The_Merge(string parameter)
    {
        var js = ClientRuntime;
        var start = js.IndexOf("const GRID_PARAMS = new Set(", StringComparison.Ordinal);
        Assert.True(start >= 0, "GRID_PARAMS is missing from the client runtime.");

        var end = js.IndexOf(");", start, StringComparison.Ordinal);
        var declaration = js[start..end];

        Assert.Contains(parameter, declaration, StringComparison.Ordinal);
    }
}
