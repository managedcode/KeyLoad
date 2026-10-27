using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Charts show 1-node and 3-node values together; every drawn bar carries its exact recorded value.</summary>
internal static class SiteIsolatedBrowserChartAssertions
{
    private const string Overview = """
        (()=>Array.from(document.querySelectorAll('#isolated-results .ov-row')).map(row=>({target:row.dataset.target,
          lines:Array.from(row.querySelectorAll('.pair[data-metric] .pair-line')).map(line=>
            line.parentElement.dataset.metric+':'+(line.classList.contains('n3')?3:1)+':'+(line.dataset.value===''?'null':line.dataset.value))})))()
        """;
    private const string Cards = """
        (()=>Array.from(document.querySelectorAll('#isolated-results .db-card')).map(card=>({target:card.dataset.target,
          empty:!!card.querySelector('.db-empty'),
          lines:Array.from(card.querySelectorAll('li[data-test] .pair-line')).map(line=>
            line.closest('li').dataset.test+':'+(line.classList.contains('n3')?3:1)+':'+(line.dataset.value===''?'null':line.dataset.value))})))()
        """;

    public static Task SortAsync(SiteBrowserCdpClient cdp, string key, CancellationToken token)
        => SiteBrowserAssertions.EvaluateSuccess(cdp, "(()=>{const b=document.querySelector('#isolated-results [data-sort=\"" + key +
            "\"]');if(!b)return false;b.click();return document.querySelector('#isolated-results [data-sort=\"" + key + "\"]').classList.contains('is-sorted');})()", token);

    /// <summary>The overview ranks every planned engine by the selected family on 1 node, unmeasured engines last.</summary>
    public static async Task AssertOverviewAsync(SiteBrowserCdpClient cdp, SiteIsolatedExpectedValues expected, string sort,
        CancellationToken token)
    {
        var rows = (await cdp.EvaluateAsync(Overview, false, token)).EnumerateArray().ToArray();
        var test = SiteIsolatedExpectedValues.General.Single(item => item.Key == sort).Test;
        var order = expected.Targets(SiteIsolatedExpectedValues.All)
            .OrderByDescending(target => expected.Value(target, test, 1) ?? (expected.Measured(target) ? -1 : -2)).ToArray();
        await Assert.That(string.Join('|', rows.Select(row => row.GetProperty(SiteIsolatedFields.Target).GetString())))
            .IsEqualTo(string.Join('|', order));
        foreach (var row in rows)
        {
            var target = row.GetProperty(SiteIsolatedFields.Target).GetString()!;
            var lines = expected.Measured(target)
                ? SiteIsolatedExpectedValues.General.SelectMany(metric => expected.Shown(target).Select(nodes =>
                    $"{metric.Key}:{nodes}:{SiteIsolatedExpectedValues.Format(expected.Value(target, metric.Test, nodes))}"))
                : [];
            await AssertLinesAsync(row, lines);
        }
    }

    /// <summary>A family view shows one card per planned engine with every test; engines without results say so.</summary>
    public static async Task AssertCardsAsync(SiteBrowserCdpClient cdp, SiteIsolatedExpectedValues expected, string family,
        CancellationToken token)
    {
        var cards = (await cdp.EvaluateAsync(Cards, false, token)).EnumerateArray().ToArray();
        var targets = expected.Targets(family);
        await Assert.That(string.Join('|', cards.Select(card => card.GetProperty(SiteIsolatedFields.Target).GetString())))
            .IsEqualTo(string.Join('|', targets));
        foreach (var card in cards)
        {
            var target = card.GetProperty(SiteIsolatedFields.Target).GetString()!;
            await Assert.That(card.GetProperty(SiteIsolatedFields.Empty).GetBoolean()).IsEqualTo(!expected.Measured(target));
            var lines = expected.Measured(target)
                ? SiteIsolatedExpectedValues.Tests.SelectMany(test => expected.Shown(target).Select(nodes =>
                    $"{test}:{nodes}:{SiteIsolatedExpectedValues.Format(expected.Value(target, test, nodes))}"))
                : [];
            await AssertLinesAsync(card, lines);
        }
    }

    private static async Task AssertLinesAsync(JsonElement element, IEnumerable<string> expected)
    {
        var actual = element.GetProperty(SiteIsolatedFields.Lines).EnumerateArray().Select(line => Normalize(line.GetString()!));
        await Assert.That(string.Join('|', actual)).IsEqualTo(string.Join('|', expected));
    }

    // Browser values are JavaScript shortest round-trip strings; C# formats the same double with "R".
    private static string Normalize(string line)
    {
        var parts = line.Split(':');
        return parts[2] == "null" ? line
            : $"{parts[0]}:{parts[1]}:{SiteIsolatedExpectedValues.Format(double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture))}";
    }
}
