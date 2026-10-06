using System.Text.Json;
using System.Text.Json.Nodes;
using F = KeyLoad.UnitTests.Features.BenchmarkComparisons.IsolatedAggregateFields;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/007 distinguishes declared exclusions from failed real workloads.</summary>
internal sealed class IsolatedAggregateUnsupportedTests
{
    [Test]
    public async Task AC_ISO_007_OnlyDeclaredNativeTopologyCanHaveNullReport()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var value = IsolatedAggregateData.Envelope();
        value[F.Worker]![F.Target] = F.Neo4j;
        value[F.Disposition] = F.UnsupportedTopology;
        value[F.Report] = null;
        value[F.Reason] = IsolatedAggregateData.Contract()[F.UnsupportedTopologies]![0]![F.Reason]!.GetValue<string>();
        var cell = Cell(F.Neo4j, F.NeoCell);
        await Assert.That(await IsolatedAggregateProbe.AcceptedAsync(value, cell, token)).IsTrue();
        value[F.Reason] = null;
        await Assert.That(await IsolatedAggregateProbe.AcceptedAsync(value, cell, token)).IsFalse();
        value[F.Reason] = F.StartupFailure;
        await Assert.That(await IsolatedAggregateProbe.AcceptedAsync(value, cell, token)).IsFalse();
    }

    [Test]
    public async Task AC_ISO_007_UnsupportedCapabilityRetainsFiveNativeClusterReports()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var value = IsolatedAggregateData.Envelope();
        value[F.Worker]![F.Target] = F.Qdrant;
        var report = value[F.Report]!.AsObject();
        report[F.Targets]![0]![F.Name] = F.Qdrant;
        foreach (var item in report[F.Cases]!.AsArray())
        {
            item![F.Target] = F.Qdrant;
            item[F.Status] = F.Unsupported;
            item[F.Detail] = F.UnsupportedCapability;
            item[F.Measurement] = null;
            item[F.Samples] = new JsonArray();
        }

        var cell = Cell(F.Qdrant, F.QdrantCell);
        await Assert.That(await IsolatedAggregateProbe.AcceptedAsync(value, cell, token)).IsTrue();
        report[F.Targets]![0]![F.Cluster]![F.Nodes] = 1;
        await Assert.That(await IsolatedAggregateProbe.AcceptedAsync(value, cell, token)).IsFalse();
        report[F.Targets]![0]![F.Cluster]![F.Nodes] = 2;
        report[F.Cases]![0]![F.Detail] = null;
        await Assert.That(await IsolatedAggregateProbe.AcceptedAsync(value, cell, token)).IsFalse();
    }

    private static JsonObject Cell(string target, string id)
    {
        var cell = JsonSerializer.SerializeToNode(IsolatedAggregateData.Cell(), IsolatedAggregateData.JsonOptions)!.AsObject();
        cell[F.Target] = target;
        cell[F.Id] = id;
        return cell;
    }
}
