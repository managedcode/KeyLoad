using System.Globalization;
using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedBrowserAssertions
{
    private const string Rows = "#isolated-results .isolated-table-scroll tbody tr";
    private const string Ready = "document.querySelectorAll('" + Rows + "').length>0 && document.querySelector('#isolated-error').hidden";
    private const string Snapshot = """
        (()=>({rows:Array.from(document.querySelectorAll('#isolated-results .isolated-table-scroll tbody tr')).map(row=>({
          name:row.cells[0].textContent,nodeCount:Number(row.cells[1].textContent),status:row.cells[2].textContent,
          value:row.dataset.value===''?null:Number(row.dataset.value),workerId:row.dataset.workerId,
          display:row.cells[3].textContent,links:Array.from(row.querySelectorAll('a')).map(a=>a.href)
        })),ranked:Array.from(document.querySelectorAll('#isolated-results .rank-row')).map(row=>row.dataset.workerId)}))()
        """;
    private static readonly (string Family, string[] Engines)[] EngineFamilies =
    [
        ("Multi-model", ["KeyLoad", "SurrealDB"]),
        ("Relational", ["PostgreSQL + pgvector", "SQL Server", "MySQL"]),
        ("Document", ["MongoDB"]),
        ("Key-value", ["Redis"]),
        ("Search", ["OpenSearch"]),
        ("Graph", ["Neo4j", "HelixDB"]),
        ("Time series", ["TimescaleDB", "InfluxDB"]),
        ("Analytics", ["ClickHouse"]),
        ("Vector", ["Qdrant"]),
        ("Queue", ["RabbitMQ"]),
        ("Event store", ["KurrentDB"]),
    ];

    public static async Task WaitAsync(SiteBrowserCdpClient cdp, CancellationToken token)
        => await Assert.That(await cdp.WaitForExpressionAsync(Ready, token)).IsTrue();

    public static Task SelectAsync(SiteBrowserCdpClient cdp, string id, object value, CancellationToken token)
    {
        var expression = "(()=>{const e=Array.from(document.querySelectorAll(" + JsonSerializer.Serialize("#isolated-" + id + " input") +
            ")).find(input=>input.value===" + JsonSerializer.Serialize(Convert.ToString(value, CultureInfo.InvariantCulture)) +
            ");if(!e)return false;e.click();return e.checked;})()";
        return SiteBrowserAssertions.EvaluateSuccess(cdp, expression, token);
    }

    /// <summary>The selection target is a database family or <c>all</c>; expected rows come from the independent oracle.</summary>
    public static async Task AssertRowsAsync(SiteBrowserCdpClient cdp, JsonElement projection,
        SiteIsolatedSelection selection, CancellationToken token)
    {
        var snapshot = await cdp.EvaluateAsync(Snapshot, false, token);
        var rows = snapshot.GetProperty(SiteIsolatedFields.Rows).EnumerateArray().ToArray();
        var expected = SiteIsolatedOracle.Rows(projection, selection with { Target = "all" })
            .Where(row => selection.Target == "all" || FamilyOf(row.Name) == selection.Target).ToArray();
        await Assert.That(rows.Length).IsEqualTo(expected.Length);
        var ranked = snapshot.GetProperty(SiteIsolatedFields.Ranked).EnumerateArray().Select(item => item.GetString());
        await Assert.That(string.Join('|', ranked))
            .IsEqualTo(string.Join('|', expected.Where(row => row.Value is not null).Select(row => row.WorkerId)));
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
            await AssertLinksAsync(links, run, oracle);
            await AssertValueAsync(row, oracle.Value);
        }
    }

    private static string FamilyOf(string engine)
        => EngineFamilies.Single(item => item.Engines.Contains(engine, StringComparer.Ordinal)).Family;

    private static async Task AssertValueAsync(JsonElement row, double? expected)
    {
        if (expected is null)
        {
            await Assert.That(row.GetProperty(SiteIsolatedFields.Value).ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(row.GetProperty(SiteIsolatedFields.Display).GetString()).IsEqualTo("—");
        }
        else
        {
            await Assert.That(row.GetProperty(SiteIsolatedFields.Value).GetDouble()).IsEqualTo(expected.Value).Within(0.000001);
            await Assert.That(row.GetProperty(SiteIsolatedFields.Display).GetString()!.StartsWith(Display(expected.Value), StringComparison.Ordinal)).IsTrue();
        }
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
