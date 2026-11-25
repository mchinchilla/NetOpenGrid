using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>
/// Reads a grid's first-paint state from its data block (<c>&lt;template data-netgrid-state="{id}"&gt;</c>), the way
/// netopengrid.js does: inert JSON a strict Content-Security-Policy host never runs and htmx never drops from a swap.
/// </summary>
internal static partial class GridState
{
    public static JsonElement Of(string html, string gridId)
    {
        var match = Block().Matches(html).FirstOrDefault(m => m.Groups["id"].Value == gridId);
        Assert.True(match is not null, $"no state block for grid '{gridId}'");
        using var document = JsonDocument.Parse(match!.Groups["json"].Value);
        return document.RootElement.Clone();
    }

    [GeneratedRegex("<template data-netgrid-state=\"(?<id>[^\"]*)\">(?<json>.*?)</template>", RegexOptions.Singleline)]
    private static partial Regex Block();
}
