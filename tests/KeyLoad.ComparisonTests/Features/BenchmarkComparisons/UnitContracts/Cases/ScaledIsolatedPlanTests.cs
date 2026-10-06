using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-SCALE-014/015: independent exact profile and composite identity oracles.</summary>
internal sealed class ScaledIsolatedPlanTests
{
    private static readonly string[] ProfileIds = ["scaled-100k-c16", "scaled-1m-c16"];
    private static readonly int[] RecordCounts = [100_000, 1_000_000];
    private static readonly string[] Scenarios = ["PointRead", "DocumentWrite", "DocumentUpdate", "DocumentDelete"];

    [Test]
    public async Task AcScale014PlansAreExactlyTheTwoBoundedCrudProfiles()
    {
        var response = await IsolatedPlanNodeProcess.ProbeAsync("create-scales");
        await Assert.That(response[IsolatedPlanFields.Ok]!.GetValue<bool>()).IsTrue();
        var plans = response[IsolatedPlanFields.Value]!.AsArray();
        await Assert.That(plans.Count).IsEqualTo(2);
        for (var index = 0; index < ProfileIds.Length; index++)
        {
            var plan = plans[index]!.AsObject();
            await Assert.That(plan[IsolatedPlanFields.Profile]!.GetValue<string>()).IsEqualTo(ProfileIds[index]);
            await Assert.That(plan[IsolatedPlanFields.Cells]!.AsArray().Count).IsEqualTo(132);
            var settings = plan[IsolatedPlanFields.ProfileSettings]!.AsObject();
            await Assert.That(settings[IsolatedPlanFields.Documents]!.GetValue<int>()).IsEqualTo(RecordCounts[index]);
            await Assert.That(settings[IsolatedPlanFields.OperationsPerCell]!.GetValue<int>()).IsEqualTo(100_000);
            await Assert.That(settings[IsolatedPlanFields.Warmup]!.GetValue<int>()).IsEqualTo(256);
            await Assert.That(settings[IsolatedPlanFields.Repetitions]!.GetValue<int>()).IsEqualTo(1);
            await Assert.That(settings[IsolatedPlanFields.Concurrency]!.GetValue<int>()).IsEqualTo(16);
            await Assert.That(settings[IsolatedPlanFields.Seed]!.GetValue<int>()).IsEqualTo(1_729);
            await Assert.That(settings[IsolatedPlanFields.PayloadBytes]!.GetValue<int>()).IsEqualTo(1_024);
            await Assert.That(settings[IsolatedPlanFields.Dimensions]!.GetValue<int>()).IsEqualTo(32);
            await Assert.That(settings[IsolatedPlanFields.TopK]!.GetValue<int>()).IsEqualTo(10);
            await Assert.That(settings[IsolatedPlanFields.TimeoutSeconds]!.GetValue<int>()).IsEqualTo(30);
            await Assert.That(settings[IsolatedPlanFields.GraphVertices]!.GetValue<int>()).IsEqualTo(0);
            await Assert.That(settings[IsolatedPlanFields.GraphFanOut]!.GetValue<int>()).IsEqualTo(0);
            await Assert.That(settings[IsolatedPlanFields.GraphDepth]!.GetValue<int>()).IsEqualTo(0);
            var cells = plan[IsolatedPlanFields.Cells]!.AsArray();
            await Assert.That(cells.All(cell => cell![IsolatedPlanFields.Profile]!.GetValue<string>() == ProfileIds[index]
                && cell[IsolatedPlanFields.Family]!.GetValue<string>() == IsolatedPlanFields.CrudFamily
                && Scenarios.Contains(cell[IsolatedPlanFields.Scenario]!.GetValue<string>(), StringComparer.Ordinal))).IsTrue();
            await Assert.That(cells.Select(cell => cell![IsolatedPlanFields.Id]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(132);
            foreach (var scenario in Scenarios)
            {
                await Assert.That(cells.Count(cell => cell![IsolatedPlanFields.Scenario]!.GetValue<string>() == scenario))
                    .IsEqualTo(33);
            }
        }

        var accepted = await IsolatedPlanNodeProcess.ProbeAsync("validate-scales", plans);
        await Assert.That(accepted[IsolatedPlanFields.Ok]!.GetValue<bool>()).IsTrue();
        plans[1]![IsolatedPlanFields.ProfileSettings]![IsolatedPlanFields.Documents] = 4_000_000;
        var rejected = await IsolatedPlanNodeProcess.ProbeAsync("validate-scales", plans);
        await Assert.That(rejected[IsolatedPlanFields.Ok]!.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task AcScale015CompositeAddsUniqueScaleIdsWithoutChangingTheControlPlan()
    {
        var control = (await IsolatedPlanNodeProcess.ProbeAsync("create"))[IsolatedPlanFields.Value]!.AsObject();
        var composite = (await IsolatedPlanNodeProcess.ProbeAsync("create-composite"))[IsolatedPlanFields.Value]!.AsObject();
        await Assert.That(composite[IsolatedPlanFields.SchemaVersion]!.GetValue<int>()).IsEqualTo(3);
        await Assert.That(JsonNode.DeepEquals(composite[IsolatedPlanFields.Control], control)).IsTrue();
        await Assert.That(control[IsolatedPlanFields.Cells]!.AsArray().Count).IsEqualTo(330);
        var scales = composite[IsolatedPlanFields.ScaledProfiles]!.AsArray();
        await Assert.That(scales.Count).IsEqualTo(2);
        var vectors = composite[IsolatedPlanFields.VectorProfiles]!.AsArray();
        await Assert.That(vectors.Count).IsEqualTo(24);
        var ids = control[IsolatedPlanFields.Cells]!.AsArray()
            .Concat(scales.SelectMany(profile => profile![IsolatedPlanFields.Cells]!.AsArray()))
            .Concat(vectors.SelectMany(profile => profile![IsolatedPlanFields.Cells]!.AsArray()))
            .Select(cell => cell![IsolatedPlanFields.Id]!.GetValue<string>()).ToArray();
        await Assert.That(ids.Length).IsEqualTo(330 + 264 + 792);
        await Assert.That(ids.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(ids.Length);
        await Assert.That(ids.Contains("keyload-n1-point-read-scaled-100k-c16", StringComparer.Ordinal)).IsTrue();
    }
}
