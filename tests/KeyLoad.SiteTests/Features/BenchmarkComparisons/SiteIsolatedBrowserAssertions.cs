using System.Globalization;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedBrowserAssertions
{
    private const string Ready = "document.querySelectorAll('#isolated-results tbody tr').length>0 && document.querySelector('#isolated-error').hidden";
    private const string Snapshot = """
        (()=>Array.from(document.querySelectorAll('#isolated-results tbody tr')).map(row=>({
          name:row.cells[0].textContent,nodeCount:Number(row.cells[1].textContent),status:row.cells[2].textContent,
          value:row.dataset.value===''?null:Number(row.dataset.value),workerId:row.dataset.workerId,
          display:row.cells[3].textContent,links:Array.from(row.querySelectorAll('a')).map(a=>a.href)
        })))()
        """;

    public static async Task WaitAsync(SiteBrowserCdpClient cdp, CancellationToken token)
        => await Assert.That(await cdp.WaitForExpressionAsync(Ready, token)).IsTrue();

    public static Task SelectAsync(SiteBrowserCdpClient cdp, string id, object value, CancellationToken token)
        => SiteBrowserAssertions.SelectValue(cdp, "#isolated-" + id,
            Convert.ToString(value, CultureInfo.InvariantCulture)!, token);

    public static async Task AssertRowsAsync(SiteBrowserCdpClient cdp, JsonElement projection,
        SiteIsolatedSelection selection, CancellationToken token)
    {
        var rows = (await cdp.EvaluateAsync(Snapshot, false, token)).EnumerateArray().ToArray();
        var expected = SiteIsolatedOracle.Rows(projection, selection);
        await Assert.That(rows.Length).IsEqualTo(expected.Length);
        var cohort = projection.GetProperty(SiteIsolatedFields.Cohort);
        var run = SiteTokens.EvidenceBase + cohort.GetProperty(SiteIsolatedFields.RunId).GetInt64().ToString(CultureInfo.InvariantCulture);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var oracle = expected[index];
            await Assert.That(row.GetProperty(SiteIsolatedFields.Name).GetString()).IsEqualTo(oracle.Name);
            await Assert.That(row.GetProperty(SiteIsolatedFields.NodeCount).GetInt32()).IsEqualTo(oracle.NodeCount);
            await Assert.That(row.GetProperty(SiteIsolatedFields.WorkerId).GetString()).IsEqualTo(oracle.WorkerId);
            await Assert.That(row.GetProperty(SiteIsolatedFields.Status).GetString()).IsEqualTo(Status(oracle.Status));
            var links = row.GetProperty(SiteIsolatedFields.Links).EnumerateArray().Select(item => item.GetString()).ToArray();
            await Assert.That(links[0]).IsEqualTo(run + "/job/" + oracle.JobId.ToString(CultureInfo.InvariantCulture));
            await Assert.That(links[1]).IsEqualTo(run + "/artifacts/" + oracle.ArtifactId.ToString(CultureInfo.InvariantCulture));
            await AssertValueAsync(row, oracle.Value);
        }
    }

    private static async Task AssertValueAsync(JsonElement row, double? expected)
    {
        if (expected is null)
        {
            await Assert.That(row.GetProperty(SiteIsolatedFields.Value).ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(row.GetProperty(SiteIsolatedFields.Display).GetString()).IsEqualTo("Unavailable");
        }
        else
        {
            await Assert.That(row.GetProperty(SiteIsolatedFields.Value).GetDouble()).IsEqualTo(expected.Value).Within(0.000001);
            await Assert.That(row.GetProperty(SiteIsolatedFields.Display).GetString()!.StartsWith(Display(expected.Value), StringComparison.Ordinal)).IsTrue();
        }
    }

    private static string Display(double value)
    {
        var exact = decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture);
        return decimal.Round(exact, 3, MidpointRounding.AwayFromZero).ToString("#,##0.###", CultureInfo.GetCultureInfo("en"));
    }

    private static string Status(string value) => value switch
    {
        "measured" => "Measured",
        "unsupported" => "Unsupported scenario",
        "unsupportedTopology" => "Unsupported native topology",
        "failed" => "Benchmark failed · no data",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
