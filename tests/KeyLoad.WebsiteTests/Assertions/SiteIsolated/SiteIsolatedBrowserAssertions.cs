using System.Globalization;
using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedBrowserAssertions
{
    private const string Rows = "#isolated-results .isolated-table-scroll tbody tr";
    private const string Ready = "document.querySelectorAll('" + Rows + "').length>0 && document.querySelector('#isolated-error').hidden";
    private const string NoData = "No data";
    private const string Snapshot = """
        (()=>Array.from(document.querySelectorAll('#isolated-results .isolated-table-scroll tbody tr')).map(row=>({
          name:row.cells[0].textContent,nodeCount:Number(row.cells[1].textContent),status:row.cells[2].textContent,
          workerId:row.dataset.workerId,links:Array.from(row.querySelectorAll('a')).map(a=>a.href),
          values:Array.from(row.querySelectorAll('td[data-test]')).map(cell=>({test:cell.dataset.test,
            value:cell.dataset.value===''?null:Number(cell.dataset.value),display:cell.textContent}))})))()
        """;

    public static async Task WaitAsync(SiteBrowserCdpClient cdp, CancellationToken token)
        => await Assert.That(await cdp.WaitForExpressionAsync(Ready, token)).IsTrue();

    public static Task SelectTypeAsync(SiteBrowserCdpClient cdp, string family, CancellationToken token)
    {
        var expression = "(()=>{const e=Array.from(document.querySelectorAll('#isolated-type input')).find(input=>input.value===" +
            JsonSerializer.Serialize(family) + ");if(!e)return false;e.click();return e.checked;})()";
        return SiteBrowserAssertions.EvaluateSuccess(cdp, expression, token);
    }

    /// <summary>The results table keeps every planned session of the selected family with exact recorded values and links.</summary>
    public static async Task AssertTableAsync(SiteBrowserCdpClient cdp, SiteIsolatedExpectedValues expected, string family,
        CancellationToken token)
    {
        var rows = (await cdp.EvaluateAsync(Snapshot, false, token)).EnumerateArray().ToArray();
        var planned = expected.Planned(family);
        await Assert.That(rows.Length).IsEqualTo(planned.Length);
        var cohort = expected.Projection.GetProperty(SiteIsolatedFields.Cohort);
        var run = SiteTokens.EvidenceBase + cohort.GetProperty(SiteIsolatedFields.RunId).GetInt64().ToString(CultureInfo.InvariantCulture);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var (id, target, nodes) = planned[index];
            var session = expected.Row(target, "load-million", nodes);
            await Assert.That(row.GetProperty(SiteIsolatedFields.Name).GetString()).IsEqualTo(target);
            await Assert.That(row.GetProperty(SiteIsolatedFields.NodeCount).GetInt32()).IsEqualTo(nodes);
            await Assert.That(row.GetProperty(SiteIsolatedFields.WorkerId).GetString()).IsEqualTo(id);
            await Assert.That(row.GetProperty(SiteIsolatedFields.Status).GetString()).IsEqualTo(Status(session.Status));
            await AssertLinksAsync([.. row.GetProperty(SiteIsolatedFields.Links).EnumerateArray().Select(item => item.GetString())], run, session);
            var values = row.GetProperty(SiteIsolatedFields.Values).EnumerateArray().ToArray();
            await Assert.That(string.Join('|', values.Select(cell => cell.GetProperty(SiteIsolatedFields.Test).GetString())))
                .IsEqualTo(string.Join('|', SiteIsolatedExpectedValues.TableColumns));
            foreach (var cell in values)
            {
                await AssertValueAsync(cell, expected.Value(target, cell.GetProperty(SiteIsolatedFields.Test).GetString()!, nodes));
            }
        }
    }

    private static async Task AssertValueAsync(JsonElement cell, double? expected)
    {
        var value = cell.GetProperty(SiteIsolatedFields.Value);
        await Assert.That(SiteIsolatedExpectedValues.Format(value.ValueKind == JsonValueKind.Null ? null : value.GetDouble()))
            .IsEqualTo(SiteIsolatedExpectedValues.Format(expected));
        await Assert.That(cell.GetProperty(SiteIsolatedFields.Display).GetString()).IsEqualTo(expected is null ? NoData : Display(expected.Value));
    }

    private static async Task AssertLinksAsync(string?[] links, string run, SiteIsolatedExpectedRow row)
    {
        if (row.JobId is null)
        {
            await Assert.That(links).HasSingleItem();
            await Assert.That(links[0]).IsEqualTo(run);
        }
        else
        {
            await Assert.That(links.Length).IsEqualTo(2);
            await Assert.That(links[0]).IsEqualTo(run + "/job/" + row.JobId.Value.ToString(CultureInfo.InvariantCulture));
            await Assert.That(links[1]).IsEqualTo(run + "/artifacts/" + row.ArtifactId!.Value.ToString(CultureInfo.InvariantCulture));
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
        "unsupportedTopology" => "Unsupported execution topology",
        "failed" => "Benchmark failed · no data",
        "missing" => "No verified result",
        "resourceIncomplete" => "Measured · incomplete telemetry",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
