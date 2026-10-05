using System.Text.Json.Nodes;
using KeyLoad.UnitTests.Features.RepositoryGovernance;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-GROUP-001/002: independent inventory and displayed-name oracles for derived matrices.</summary>
internal static class IsolatedDatabaseMatrixAssertions
{
    private const string ControlProfile = "intensive-1k-c16";
    private static readonly string[] ScaleProfiles = ["scaled-100k-c16", "scaled-1m-c16", "scaled-5m-c16"];
    private static readonly string[] CanonicalFields =
    [
        IsolatedPlanFields.Id, IsolatedPlanFields.Target, IsolatedPlanFields.NodeCount,
        IsolatedPlanFields.Scenario, IsolatedPlanFields.Profile, IsolatedPlanFields.Family
    ];

    internal static async Task VerifyAsync(JsonObject matrices, JsonNode plan, JsonArray scaledPlans)
    {
        await Assert.That(matrices.Count).IsEqualTo(9);
        await Assert.That(matrices.Select(static entry => entry.Key)
            .SequenceEqual(WorkflowDatabaseGroups.Entries.Select(static entry => entry.Key))).IsTrue();
        var cells = plan[IsolatedPlanFields.Cells]!.AsArray();
        var allCells = new JsonArray(cells.Select(static cell => cell!.DeepClone()).ToArray());
        foreach (var profile in scaledPlans)
        {
            foreach (var cell in profile![IsolatedPlanFields.Cells]!.AsArray())
            { allCells.Add(cell!.DeepClone()); }
        }
        var rows = new List<JsonNode>();
        foreach (var (key, target) in WorkflowDatabaseGroups.Entries)
        {
            var matrix = matrices[key]!.AsObject();
            await Assert.That(matrix.Count).IsEqualTo(1);
            var include = matrix[IsolatedPlanFields.Include]!.AsArray();
            await VerifyDatabaseAsync(include, target, allCells);
            rows.AddRange(include.Select(static row => row!));
        }

        await Assert.That(rows.Count).IsEqualTo(621);
        await Assert.That(rows.Count(static row => row[IsolatedPlanFields.Preflight]!.GetValue<bool>())).IsEqualTo(27);
        await Assert.That(rows.Count(static row => !row[IsolatedPlanFields.Preflight]!.GetValue<bool>())).IsEqualTo(594);
        await Assert.That(rows.Select(static row => row[IsolatedPlanFields.JobName]!.GetValue<string>())
            .Distinct(StringComparer.Ordinal).Count()).IsEqualTo(621);
        await Assert.That(rows.Select(static row => (row[IsolatedPlanFields.Id]!.GetValue<string>(), row[IsolatedPlanFields.Preflight]!.GetValue<bool>()))
            .Distinct().Count()).IsEqualTo(621);
    }

    private static async Task VerifyDatabaseAsync(JsonArray rows, string target, JsonArray cells)
    {
        await Assert.That(rows.Count).IsEqualTo(69);
        await Assert.That(rows.Count <= 256).IsTrue();
        var checks = rows.Where(static row => row![IsolatedPlanFields.Preflight]!.GetValue<bool>()).ToArray();
        var workloads = rows.Where(static row => !row![IsolatedPlanFields.Preflight]!.GetValue<bool>()).ToArray();
        var control = workloads.Where(row => row![IsolatedPlanFields.Profile]!.GetValue<string>() == ControlProfile).ToArray();
        await Assert.That(checks.Length).IsEqualTo(3);
        await Assert.That(workloads.Length).IsEqualTo(66);
        await Assert.That(control.Length).IsEqualTo(30);
        foreach (var profile in ScaleProfiles)
        {
            var scale = workloads.Where(row => row![IsolatedPlanFields.Profile]!.GetValue<string>() == profile).ToArray();
            await Assert.That(scale.Length).IsEqualTo(12);
            foreach (var nodes in new[] { 1, 2, 3 })
            {
                await Assert.That(scale.Count(row => row![IsolatedPlanFields.NodeCount]!.GetValue<int>() == nodes)).IsEqualTo(4);
            }
        }
        var expected = cells.Where(cell => cell![IsolatedPlanFields.Target]!.GetValue<string>() == target).ToArray();
        await Assert.That(workloads.Select(static row => row![IsolatedPlanFields.Id]!.GetValue<string>()).Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Select(static cell => cell![IsolatedPlanFields.Id]!.GetValue<string>()).Order(StringComparer.Ordinal),
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        foreach (var nodes in new[] { 1, 2, 3 })
        {
            await Assert.That(checks.Count(row => row![IsolatedPlanFields.NodeCount]!.GetValue<int>() == nodes)).IsEqualTo(1);
            await Assert.That(workloads.Count(row => row![IsolatedPlanFields.NodeCount]!.GetValue<int>() == nodes)).IsEqualTo(22);
        }

        foreach (var row in rows)
        {
            await VerifyRowAsync(row!.AsObject(), target, cells);
        }
    }

    private static async Task VerifyRowAsync(JsonObject row, string target, JsonArray cells)
    {
        await Assert.That(row.Count).IsEqualTo(12);
        await Assert.That(row[IsolatedPlanFields.Target]!.GetValue<string>()).IsEqualTo(target);
        var id = row[IsolatedPlanFields.Id]!.GetValue<string>();
        var canonical = cells.Single(cell => cell![IsolatedPlanFields.Id]!.GetValue<string>() == id)!;
        foreach (var field in CanonicalFields)
        {
            await Assert.That(JsonNode.DeepEquals(row[field], canonical[field])).IsTrue();
        }

        var preflight = row[IsolatedPlanFields.Preflight]!.GetValue<bool>();
        var nodes = row[IsolatedPlanFields.NodeCount]!.GetValue<int>();
        var scenario = row[IsolatedPlanFields.Scenario]!.GetValue<string>();
        var profile = row[IsolatedPlanFields.Profile]!.GetValue<string>();
        if (preflight)
        {
            await Assert.That(scenario).IsEqualTo(CheckScenario(target));
        }

        var nodeLabel = nodes == 1 ? "1 node" : $"{nodes} nodes";
        var scaleSuffix = !preflight && profile != ControlProfile ? " / " + profile : string.Empty;
        var label = (preflight ? $"Check / {nodeLabel}" : $"{nodeLabel} / {ScenarioLabel(scenario)}") + scaleSuffix;
        await Assert.That(row[IsolatedPlanFields.Label]!.GetValue<string>()).IsEqualTo(label);
        await Assert.That(row[IsolatedPlanFields.JobName]!.GetValue<string>()).IsEqualTo(target + " / " + label);
        var scaleProfile = row[IsolatedPlanFields.ScaleProfile]?.GetValue<string>();
        await Assert.That(scaleProfile).IsEqualTo(!preflight && profile != ControlProfile ? profile : null);
        await Assert.That(row[IsolatedPlanFields.ArtifactPrefix]!.GetValue<string>()).IsEqualTo(preflight ? "comparison-preflight-" : "comparison-worker-");
        await Assert.That(row[IsolatedPlanFields.QualificationPrefix]!.GetValue<string>()).IsEqualTo(preflight
            ? "comparison-preflight-qualification-" : "comparison-case-qualification-");
    }

    private static string CheckScenario(string target) => target switch
    {
        "Qdrant" => "VectorExact",
        "RabbitMQ" => "QueueCycle",
        "KurrentDB" => "StreamAppend",
        _ => "PointRead"
    };

    private static string ScenarioLabel(string scenario) => scenario switch
    {
        "PointRead" => "Point read",
        "DocumentWrite" => "Document write",
        "DocumentUpdate" => "Document update",
        "DocumentDelete" => "Document delete",
        "VectorExact" => "Exact vector search",
        "QueueCycle" => "Queue cycle",
        "GraphNeighbors" => "Graph neighbors",
        "GraphTraverse" => "Graph traversal",
        "StreamAppend" => "Stream append",
        "StreamRead" => "Stream read",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };
}
