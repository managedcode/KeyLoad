using System.Globalization;
using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Independent per-session values for every displayed test, both topologies and the whole mixed test rate.</summary>
internal sealed class SiteIsolatedExpectedValues
{
    internal const string All = "all";
    internal const string Mixed = "mixed";
    internal static readonly string[] Tests = ["load-million", "index", "sequential-read", "random-read", "search",
        "mixed/create", "mixed/delete", "mixed/update", "mixed/read", "mixed/search", "verify-final"];
    internal static readonly string[] Columns = [.. Tests, Mixed];
    internal static readonly string[] TableColumns = [.. Tests[..5], Mixed, .. Tests[5..]];
    internal static readonly int[] Nodes = [1, 3];
    internal static readonly (string Key, string Test)[] General =
        [("write", "load-million"), ("read", "random-read"), ("search", "search"), (Mixed, Mixed)];
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
    private readonly Dictionary<(string Test, int Nodes), Dictionary<string, SiteIsolatedExpectedRow>> rows = [];

    public SiteIsolatedExpectedValues(JsonElement projection)
    {
        Projection = projection;
        foreach (var nodes in Nodes)
        {
            foreach (var test in Columns)
            {
                rows[(test, nodes)] = SiteIsolatedOracle.Rows(projection, new(test, nodes, "session", Metric(test), All))
                    .ToDictionary(row => row.WorkerId, StringComparer.Ordinal);
            }
        }
    }

    public JsonElement Projection { get; }

    public static string Metric(string test) => test is "index" or "verify-final" or Mixed ? "duration" : "throughput";

    public static string FamilyOf(string engine)
        => EngineFamilies.Single(item => item.Engines.Contains(engine, StringComparer.Ordinal)).Family;

    public static IEnumerable<string> Families => EngineFamilies.Select(item => item.Family);

    public (string Id, string Target, int Nodes)[] Planned(string family)
        => [.. Projection.GetProperty(SiteIsolatedFields.Plan).GetProperty(SiteIsolatedFields.Workers).EnumerateArray()
            .Select(worker => (worker.GetProperty(SiteIsolatedFields.Id).GetString()!, worker.GetProperty(SiteIsolatedFields.Target).GetString()!,
                worker.GetProperty(SiteIsolatedFields.NodeCount).GetInt32()))
            .Where(worker => family == All || FamilyOf(worker.Item2) == family)];

    public string[] Targets(string family) => [.. Planned(family).Select(worker => worker.Target).Distinct(StringComparer.Ordinal)];

    public SiteIsolatedExpectedRow Row(string target, string test, int nodes)
        => rows[(test, nodes)][Planned(All).Single(worker => worker.Target == target && worker.Nodes == nodes).Id];

    public double? Value(string target, string test, int nodes)
    {
        var row = Row(target, test, nodes);
        return test != Mixed || row.Value is null ? row.Value : MixedSuccesses(row.WorkerId) / row.Value.Value;
    }

    public bool Recorded(string target, int nodes) => Columns.Any(test => Value(target, test, nodes) is not null);

    public bool Measured(string target) => Nodes.Any(nodes => Recorded(target, nodes));

    public int[] Shown(string target) => Recorded(target, 3) ? Nodes : [1];

    public static string Format(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "null";

    private double MixedSuccesses(string workerId)
        => Projection.GetProperty(SiteIsolatedFields.Workers).EnumerateArray()
            .Single(worker => worker.GetProperty(SiteIsolatedFields.Id).GetString() == workerId)
            .GetProperty(SiteIsolatedFields.Reports).EnumerateArray()
            .Single(report => report.GetProperty(SiteIsolatedFields.CaseId).GetString() == Mixed)
            .GetProperty(SiteIsolatedFields.Lanes).EnumerateArray()
            .Sum(lane => lane.GetProperty(SiteIsolatedFields.Measurement).GetProperty(SiteIsolatedFields.Successes).GetInt64());
}
