using System.Text.RegularExpressions;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;
using NetOpenGrid.Infrastructure.Rendering;
using NetOpenGrid.Infrastructure.Runtime;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class BulkActionsRenderingTests
{
    private sealed record Entry(string Id, string Status);

    private static readonly Entry[] Entries =
    [
        new("d&1", "draft"),
        new("p2", "posted"),
        new("v3", "void"),
        new("a4", "approved")
    ];

    private static GridOptionsBuilder<Entry> Builder() => new GridOptionsBuilder<Entry>()
        .WithId("entries")
        .AddColumn("id", e => e.Id)
        .AddColumn("status", e => e.Status)
        .EnableRowSelection(e => e.Id);

    private static GridOptions<Entry> BulkOptions() => Builder()
        .RowSelectable(e => e.Status != "void")
        .AddBulkAction("post", a => a
            .Label("Post selected")
            .Icon("fas fa-check-double")
            .Tone(BulkActionTone.Primary)
            .When(e => e.Status == "draft"))
        .AddBulkAction("approve", a => a
            .Label("Approve <selected>")
            .Tone(BulkActionTone.Success)
            .Confirm()
            .When(e => e.Status == "posted"))
        .AddBulkAction("tag", a => a.Label("Tag").Tone(BulkActionTone.Neutral))
        .AddBulkAction("purge", a => a.Label("Purge").Tone(BulkActionTone.Danger))
        .Build();

    private static GridHtmlRenderer<Entry> Renderer(GridOptions<Entry> options, NetOpenGridLocalizationOptions? locale = null) =>
        new(options, new NetOpenGridAssetOptions(), locale);

    private static GridExecutionResult<Entry> Page() =>
        new(GridQuery.Empty, new PageResult<Entry>(Entries, Entries.Length, 1, 10));

    private static string[] Rows(string html) =>
        Regex.Matches(html, "<tr.*?</tr>", RegexOptions.Singleline).Select(m => m.Value).ToArray();

    private static string OpenTag(string row) => Regex.Match(row, "<tr[^>]*>").Value;

    private static string SelectionBar(string html) =>
        Regex.Match(html, "<div x-show=\"selected.length > 0\".*?</form>.*?</div>", RegexOptions.Singleline).Value;

    private static string Js() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "NetOpenGrid.Infrastructure", "Assets", "netopengrid.js"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NetOpenGrid.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // ---- Selection bar ---------------------------------------------------------------------------

    [Fact]
    public async Task Bar_Renders_One_Button_Per_Bulk_Action_Before_The_Export()
    {
        var bar = SelectionBar(await Renderer(BulkOptions()).RenderShellAsync(Page()));

        Assert.Contains(
            "<button type=\"button\" class=\"btn-bulk btn-bulk-primary\" data-bulk-action=\"post\" data-label=\"Post selected\" " +
            ":disabled=\"bulkDisabled('post')\" :title=\"bulkHint('post')\" @click=\"bulkAction('post', $el)\">" +
            "<i class=\"fas fa-check-double\" aria-hidden=\"true\"></i><span>Post selected</span></button>",
            bar);

        var names = Regex.Matches(bar, "data-bulk-action=\"([^\"]+)\"").Select(m => m.Groups[1].Value);
        Assert.Equal(["post", "approve", "tag", "purge"], names);
        Assert.True(bar.IndexOf("data-bulk-action=\"purge\"", StringComparison.Ordinal) <
                    bar.IndexOf("name=\"ids\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bar_Buttons_Carry_Tone_Confirm_And_Encoded_Labels()
    {
        var bar = SelectionBar(await Renderer(BulkOptions()).RenderShellAsync(Page()));

        Assert.Contains("class=\"btn-bulk btn-bulk-success\" data-bulk-action=\"approve\"", bar);
        Assert.Contains("class=\"btn-bulk btn-bulk-neutral\" data-bulk-action=\"tag\"", bar);
        Assert.Contains("class=\"btn-bulk btn-bulk-danger\" data-bulk-action=\"purge\"", bar);

        // Confirm() marks the button; the host confirms on its own everywhere else.
        Assert.Contains("data-label=\"Approve &lt;selected&gt;\" data-confirm :disabled=", bar);
        Assert.Single(Regex.Matches(bar, " data-confirm "));
        Assert.Contains("<span>Approve &lt;selected&gt;</span>", bar);
        Assert.DoesNotContain("<selected>", bar);

        // No icon configured, no <i>.
        Assert.Single(Regex.Matches(bar, "<i "));
    }

    [Fact]
    public async Task Bar_Labels_Go_Through_The_Text_Localizer()
    {
        var locale = new NetOpenGridLocalizationOptions
        {
            TextLocalizer = text => text == "Post selected" ? "Contabilizar seleccionados" : text
        };

        var bar = SelectionBar(await Renderer(BulkOptions(), locale).RenderShellAsync(Page()));

        Assert.Contains("data-label=\"Contabilizar seleccionados\"", bar);
        Assert.Contains("<span>Contabilizar seleccionados</span>", bar);
        Assert.DoesNotContain("Post selected", bar);
    }

    [Fact]
    public async Task Bar_Is_Disabled_As_A_Whole_While_The_Host_Is_Busy()
    {
        var bar = SelectionBar(await Renderer(BulkOptions()).RenderShellAsync(Page()));

        Assert.Contains("<button type=\"submit\" class=\"btn-primary-sm\" :disabled=\"bulkBusy\">", bar);
        Assert.Matches(":disabled=\"bulkBusy\" @click=\"clearSelection\\(\\)\"", bar);
    }

    [Fact]
    public async Task Without_Bulk_Actions_The_Bar_Only_Exports()
    {
        var html = await Renderer(Builder().Build()).RenderShellAsync(Page());

        Assert.DoesNotContain("data-bulk-action", html);
        Assert.DoesNotContain("data-bulk=", html);
        Assert.Contains("name=\"ids\"", SelectionBar(html));
    }

    // ---- Selection scope ---------------------------------------------------------------------------

    [Fact]
    public async Task Selection_Survives_Paging_By_Default()
    {
        var html = await Renderer(BulkOptions()).RenderShellAsync(Page());

        Assert.Contains("x-data=\"netgrid('entries')\" class=\"space-y-4\">", html);
        Assert.DoesNotContain("data-selection-scope", html);
    }

    [Fact]
    public async Task Page_Scope_Is_Announced_On_The_Grid_Root()
    {
        var options = Builder().WithSelectionScope(GridSelectionScope.Page).Build();
        var html = await Renderer(options).RenderShellAsync(Page());

        Assert.Contains("x-data=\"netgrid('entries')\" class=\"space-y-4\" data-selection-scope=\"page\">", html);
    }

    // ---- Rows ------------------------------------------------------------------------------------

    [Fact]
    public async Task Rows_List_The_Bulk_Actions_They_Admit()
    {
        var rows = Rows(await Renderer(BulkOptions()).RenderRowsAsync(Page()));

        // Unconditional actions are always listed; conditional ones only where When(...) holds.
        Assert.Contains("data-id=\"d&amp;1\" data-bulk=\"post tag purge\"", OpenTag(rows[0]));
        Assert.Contains("data-id=\"p2\" data-bulk=\"approve tag purge\"", OpenTag(rows[1]));
        Assert.Contains("data-id=\"a4\" data-bulk=\"tag purge\"", OpenTag(rows[3]));
    }

    [Fact]
    public async Task Rows_Skip_Data_Bulk_When_No_Action_Is_Conditional()
    {
        var options = Builder().AddBulkAction("tag").Build();
        var rows = Rows(await Renderer(options).RenderRowsAsync(Page()));

        Assert.All(rows, row => Assert.DoesNotContain("data-bulk", row));
        Assert.Contains("data-id=\"p2\"", OpenTag(rows[1]));
    }

    [Fact]
    public async Task Unselectable_Rows_Get_A_Disabled_Checkbox_And_No_Key()
    {
        var rows = Rows(await Renderer(BulkOptions()).RenderRowsAsync(Page()));
        var blocked = rows[2];

        // No data-id: "select all" (tr[data-id]) and the selection events never see the row.
        Assert.DoesNotContain("data-id", OpenTag(blocked));
        Assert.DoesNotContain("data-bulk", OpenTag(blocked));
        Assert.Contains(
            "<input type=\"checkbox\" disabled class=\"h-4 w-4 cursor-not-allowed opacity-40\" aria-label=\"Select row\" title=\"This row cannot be selected\">",
            blocked);
        Assert.DoesNotContain("toggleSelection", blocked);

        // The cell stays, so the columns keep lining up with the header.
        Assert.Equal(3, Regex.Matches(blocked, "<td").Count);
        Assert.Equal(3, Regex.Matches(rows[0], "<td").Count);
        Assert.Contains("@change=\"toggleSelection($el.closest('tr').dataset.id)\" :checked=\"isSelected('d&amp;1')\"", rows[0]);
    }

    [Fact]
    public async Task Grouped_Rows_Carry_The_Same_Selection_Markup()
    {
        var group = new GridGroup<Entry>("status=draft", "draft", 0, 2, [], [Entries[0], Entries[2]]);
        var html = await Renderer(BulkOptions()).RenderGroupedRowsAsync(new GroupedPageResult<Entry>([group], 1, 1, 10));

        Assert.Contains("data-id=\"d&amp;1\" data-bulk=\"post tag purge\"", html);
        Assert.Contains("title=\"This row cannot be selected\"", html);
    }

    // ---- i18n ------------------------------------------------------------------------------------

    [Fact]
    public async Task Spanish_Preset_Translates_The_New_Strings()
    {
        var locale = new NetOpenGridLocalizationOptions().UseCulture("es");
        var html = await Renderer(BulkOptions(), locale).RenderShellAsync(Page());

        Assert.Contains("title=\"Esta fila no se puede seleccionar\"", html);
        Assert.Contains("\"bulk.unavailable\":\"No disponible para algunas de las filas seleccionadas\"", html);
        Assert.Contains("\"bulk.confirm.many\":", html);
    }

    [Fact]
    public void Every_English_String_Has_Its_Spanish_Pair()
    {
        var english = NetOpenGridLocalizationOptions.PresetFor("en");
        var spanish = NetOpenGridLocalizationOptions.PresetFor("es");

        Assert.Empty(english.Keys.Except(spanish.Keys));
        Assert.Empty(spanish.Keys.Except(english.Keys));
        Assert.All(spanish, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));

        // Placeholders ({n}, {action}...) must survive the translation.
        foreach (var (key, text) in english)
        {
            var expected = Regex.Matches(text, "\\{\\w+\\}").Select(m => m.Value).Order();
            var actual = Regex.Matches(spanish[key], "\\{\\w+\\}").Select(m => m.Value).Order();
            Assert.True(expected.SequenceEqual(actual), key);
        }
    }

    [Fact]
    public void Client_Fallback_Strings_Exist_In_The_Server_Table()
    {
        var js = Js();
        var fallback = Regex.Match(js, "const FALLBACK = \\{(.*?)\\};", RegexOptions.Singleline).Groups[1].Value;
        var keys = Regex.Matches(fallback, "'([a-zA-Z.\\-]+)':").Select(m => m.Groups[1].Value).ToArray();

        Assert.Contains("bulk.confirm.one", keys);
        Assert.Contains("bulk.unavailable", keys);
        Assert.All(keys, key => Assert.True(NetOpenGridLocalizationOptions.PresetFor("en").ContainsKey(key), key));
    }

    // ---- Client contract (netopengrid.js) ----------------------------------------------------------
    // The repo has no JS runtime in its test stack, so these pin the contract in the source: the
    // events, their payloads and the origin rules. Behaviour is checked in a browser against the sample.

    [Fact]
    public void Js_Raises_The_Outgoing_Events_With_Their_Payloads()
    {
        var js = Js();

        Assert.Contains("this.notifyHost('netgrid:bulk-action', { grid: this.id, action, keys, count: keys.length });", js);
        Assert.Contains("this.notifyHost('netgrid:selection', { grid: this.id, keys, count: keys.length });", js);
        Assert.Contains("this.notifyHost('netgrid:action', { grid: this.id, action, key });", js);
        Assert.Contains("this.root().dispatchEvent(new CustomEvent(type, { bubbles: true, detail }));", js);

        // The selection event follows every change of the selection, and only real ones.
        Assert.Contains("this.$watch('selected', () => this.syncSelection());", js);
        Assert.Contains("if (announced !== sel.announced) {", js);
    }

    [Fact]
    public void Js_Never_Posts_To_Any_Origin_And_Only_Listens_To_Its_Own()
    {
        var js = Js();

        var posts = Regex.Matches(js, "\\.postMessage\\(([^;]*)\\);").Select(m => m.Groups[1].Value).ToArray();
        Assert.NotEmpty(posts);
        Assert.All(posts, args => Assert.EndsWith(", window.location.origin", args));
        Assert.DoesNotMatch("postMessage\\([^;]*['\"]\\*['\"]", js);

        var listeners = Regex.Matches(js, "addEventListener\\('message', \\(e\\) => \\{(.*?)\\}\\);", RegexOptions.Singleline);
        Assert.NotEmpty(listeners);
        Assert.All(listeners, m => Assert.StartsWith(
            "if (e.origin !== window.location.origin) return;", m.Groups[1].Value.Trim()));
    }

    [Fact]
    public void Js_Accepts_The_Host_Messages_As_Dom_Events_And_Post_Messages()
    {
        var js = Js();

        Assert.Contains("const HOST_EVENTS = ['netgrid:refresh', 'netgrid:clear-selection', 'netgrid:busy'];", js);
        Assert.Contains("document.addEventListener(type, (e) => onHostMessage(type, e.detail));", js);
        Assert.Contains("if (HOST_EVENTS.includes(e.data?.type)) onHostMessage(e.data.type, e.data);", js);
        Assert.Contains("if (data?.grid && data.grid !== this.id) return;", js);

        Assert.Contains("if (data?.keepSelection === false) this.clearSelection();", js);
        Assert.Contains("this.bulkBusy = data?.busy === true;", js);
    }

    [Fact]
    public void Js_Applies_The_Selection_Scope_After_Every_Swap()
    {
        var js = Js();

        Assert.Contains("state.afterRowsSwap();", js);
        Assert.Contains("state.isVirtual?.() ? 1 : state.page, state.pageSize, state.sorts, state.filters, state.q, state.groupBy", js);
        Assert.Contains("this.selectionScope = this.$el.dataset.selectionScope === 'page' ? 'page' : 'all';", js);

        // Page scope: a different query empties the selection, the same one drops the keys that are gone.
        Assert.Matches(
            "if \\(this\\.selectionScope === 'page'\\) \\{\\s+if \\(!sameQuery\\) this\\.clearSelection\\(\\);\\s+" +
            "else if \\(!this\\.isVirtual\\(\\)\\) this\\.pruneSelection\\(\\(key\\) => !live\\.has\\(key\\)\\);",
            js);

        // Default scope: nothing is emptied; only a refresh the host asked for drops keys, and only
        // the ones that were on screen and are gone.
        Assert.Matches(
            "\\} else if \\(sameQuery && sel\\.hostRefresh && !this\\.isVirtual\\(\\)\\) \\{\\s+const wasVisible = sel\\.visible;\\s+" +
            "this\\.pruneSelection\\(\\(key\\) => wasVisible\\.has\\(key\\) && !live\\.has\\(key\\)\\);",
            js);
        Assert.Contains("sel.hostRefresh = true;", js);

        // "Select all" works on tr[data-id], which unselectable rows do not carry.
        Assert.Contains("body.querySelectorAll('tr[data-id]')", js);
    }

    [Fact]
    public void Js_Disables_A_Bulk_Action_Some_Selected_Row_Does_Not_Admit()
    {
        var js = Js();

        Assert.Contains("return this.bulkBusy || this.selected.length === 0 || !this.bulkAdmits(action);", js);
        Assert.Contains("allowed = allowed === null ? names : allowed.filter((name) => names.includes(name));", js);
        Assert.Contains("if (this.bulkDisabled(action)) return;", js);
    }
}
