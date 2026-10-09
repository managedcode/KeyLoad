using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-002/006/007: preflight is an exact subset of the intensive canonical cohort.</summary>
internal sealed class IsolatedPreflightTests
{
    private const string Preflight = "preflight";
    private const string PointRead = "PointRead";
    private const string Qdrant = "Qdrant";
    private const string RabbitMq = "RabbitMQ";
    private const string KurrentDb = "KurrentDB";
    private static readonly Dictionary<string, string> Specialized = new(StringComparer.Ordinal)
    {
        [Qdrant] = "VectorExact",
        [RabbitMq] = "QueueCycle",
        [KurrentDb] = "StreamAppend"
    };

    [Test]
    public async Task AcIso006PreflightRetainsAllEngineNodePairsWithIdenticalIntensiveOptions()
    {
        var plan = (await IsolatedPlanNodeProcess.ProbeAsync("create"))[IsolatedPlanFields.Value]!;
        var response = await IsolatedPlanNodeProcess.ProbeAsync(Preflight, plan);
        await Assert.That(response[IsolatedPlanFields.Ok]!.GetValue<bool>()).IsTrue();
        var cells = response[IsolatedPlanFields.Value]![IsolatedPlanFields.Include]!.AsArray();
        await Assert.That(cells.Count).IsEqualTo(22);
        await Assert.That(cells.Select(cell => cell![IsolatedPlanFields.Id]!.GetValue<string>()).Distinct().Count()).IsEqualTo(22);
        foreach (var cell in cells)
        {
            var target = cell![IsolatedPlanFields.Target]!.GetValue<string>();
            await Assert.That(cell[IsolatedPlanFields.Scenario]!.GetValue<string>())
                .IsEqualTo(Specialized.GetValueOrDefault(target, PointRead));
            await Assert.That(plan[IsolatedPlanFields.Cells]!.AsArray().Count(original => JsonNode.DeepEquals(original, cell))).IsEqualTo(1);
        }
        await Assert.That(cells.GroupBy(cell => cell![IsolatedPlanFields.Target]!.GetValue<string>())
            .All(group => group.Select(cell => cell![IsolatedPlanFields.NodeCount]!.GetValue<int>()).Order().SequenceEqual([1, 3]))).IsTrue();
    }

    [Test]
    public async Task AcIso007PreflightRejectsAlteredCanonicalOptionsBeforeResourceAllocation()
    {
        var plan = (await IsolatedPlanNodeProcess.ProbeAsync("create"))[IsolatedPlanFields.Value]!;
        plan[IsolatedPlanFields.Options]![IsolatedPlanFields.Operations] = 1;
        var response = await IsolatedPlanNodeProcess.ProbeAsync(Preflight, plan);
        await Assert.That(response[IsolatedPlanFields.Ok]!.GetValue<bool>()).IsFalse();
    }
}
