using System.Text.RegularExpressions;
using NetOpenGrid.Application.Binding;
using NetOpenGrid.Infrastructure.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>
/// Assertions here are attribute-order-independent on purpose: HTML attribute order is
/// semantically meaningless, so the property under test is "the responsive class reaches
/// both the header cell and its matching body cell", not "class is the Nth attribute".
/// </summary>
public class ResponsiveColumnTests : IClassFixture<GridHostFixture>
{
    private readonly GridHostFixture _fixture;
    public ResponsiveColumnTests(GridHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Header_And_Cell_Carry_The_Same_Responsive_Class()
    {
        var runtime = _fixture.Services.GetRequiredKeyedService<IGridRuntime>("responsive");
        var html = await runtime.RenderFragmentAsync(GridRequestValues.Empty);

        var (headHtml, bodyHtml) = SplitHeadAndBody(html);

        // one <th> and at least one <td> for the Lg column, wherever "class" sits on the tag.
        var thClass = ExtractClassAttribute(headHtml, "data-field=\"region\"");
        var tdClass = ExtractClassAttribute(bodyHtml, "data-field=\"region\"");

        Assert.Contains("hidden lg:table-cell", thClass, StringComparison.Ordinal);
        Assert.Contains("hidden lg:table-cell", tdClass, StringComparison.Ordinal);
    }

    [Fact]
    public async Task None_Breakpoint_Leaves_Class_Attribute_Unaffected()
    {
        var runtime = _fixture.Services.GetRequiredKeyedService<IGridRuntime>("responsive");
        var html = await runtime.RenderFragmentAsync(GridRequestValues.Empty);

        var (headHtml, bodyHtml) = SplitHeadAndBody(html);

        // "code" has no HideBelow() call -> ResponsiveBreakpoint.None -> ResponsiveClass()
        // returns string.Empty, so concatenation must be a pure no-op: no leaked "hidden ..."
        // token and no doubled space at the splice point.
        var thClass = ExtractClassAttribute(headHtml, "data-field=\"code\"");
        var tdClass = ExtractClassAttribute(bodyHtml, "data-field=\"code\"");

        Assert.DoesNotContain("hidden", thClass, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", tdClass, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", thClass, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", tdClass, StringComparison.Ordinal);
        Assert.False(thClass.StartsWith(' '), $"th class should not have a stray leading space: '{thClass}'");
    }

    /// <summary>Splits a rendered fragment at &lt;/thead&gt; so the same data-field marker
    /// can be resolved unambiguously to either its header cell or its body cell.</summary>
    private static (string Head, string Body) SplitHeadAndBody(string html)
    {
        var theadEnd = html.IndexOf("</thead>", StringComparison.Ordinal);
        Assert.True(theadEnd >= 0, "expected a </thead> in the rendered fragment");
        return (html[..theadEnd], html[theadEnd..]);
    }

    /// <summary>
    /// Locates the tag containing <paramref name="marker"/> (e.g. a data-field attribute)
    /// and returns its "class" attribute value, regardless of where "class" sits among the
    /// tag's other attributes.
    /// </summary>
    private static string ExtractClassAttribute(string html, string marker)
    {
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"expected to find '{marker}' in html");

        var tagStart = html.LastIndexOf('<', markerIndex);
        var tagEnd = html.IndexOf('>', markerIndex);
        Assert.True(tagStart >= 0 && tagEnd > tagStart, $"could not locate the tag containing '{marker}'");

        var tag = html[tagStart..(tagEnd + 1)];
        var match = Regex.Match(tag, "class=\"([^\"]*)\"");
        Assert.True(match.Success, $"expected a class attribute on tag: {tag}");
        return match.Groups[1].Value;
    }
}
