using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-002/007: malformed closed plans cannot schedule a partial or foreign cohort.</summary>
internal sealed class IsolatedPlanRejectionTests
{
    [Test]
    [Arguments("empty")]
    [Arguments("missing")]
    [Arguments("duplicate")]
    [Arguments("unknown-target")]
    [Arguments("zero-nodes")]
    [Arguments("two-nodes")]
    [Arguments("four-nodes")]
    [Arguments("text-nodes")]
    [Arguments("unknown-scenario")]
    [Arguments("foreign-profile")]
    [Arguments("unsafe-id")]
    [Arguments("native-label")]
    [Arguments("extra-plan-field")]
    [Arguments("options-drift")]
    [Arguments("missing-matrix")]
    [Arguments("duplicate-matrix")]
    [Arguments("foreign-matrix")]
    [Arguments("wrong-family")]
    [Arguments("schema-drift")]
    public async Task AcIso002InvalidClosedPlanIsRejected(string corruption)
    {
        var plan = (await IsolatedPlanNodeProcess.ProbeAsync("create"))[IsolatedPlanFields.Value]!.AsObject();
        CorruptPlan(plan, corruption);
        var rejected = await IsolatedPlanNodeProcess.ProbeAsync("validate", plan);
        await Assert.That(rejected[IsolatedPlanFields.Ok]!.GetValue<bool>()).IsFalse();
        await Assert.That(rejected.ContainsKey(IsolatedPlanFields.Value)).IsFalse();
    }

    [Test]
    [Arguments("empty-targets")]
    [Arguments("unknown-target")]
    [Arguments("duplicate-target")]
    [Arguments("missing-nodes")]
    [Arguments("two-nodes")]
    [Arguments("fake-nodes")]
    [Arguments("duplicate-scenario")]
    [Arguments("unknown-scenario")]
    [Arguments("missing-options")]
    [Arguments("changed-options")]
    [Arguments("extra-options")]
    [Arguments("native-label")]
    [Arguments("profile-drift")]
    public async Task AcIso002NoncanonicalContractIsRejectedBeforePlanning(string corruption)
    {
        var contract = JsonNode.Parse(await File.ReadAllTextAsync(IsolatedPlanNodeProcess.ContractPath,
            TestContext.Current!.Execution.CancellationToken))!.AsObject();
        CorruptContract(contract, corruption);
        var rejected = await IsolatedPlanNodeProcess.ProbeAsync("create", contract: contract);
        await Assert.That(rejected[IsolatedPlanFields.Ok]!.GetValue<bool>()).IsFalse();
        await Assert.That(rejected.ContainsKey(IsolatedPlanFields.Value)).IsFalse();
    }

    private static void CorruptPlan(JsonObject plan, string corruption)
    {
        var cells = plan[IsolatedPlanFields.Cells]!.AsArray();
        var first = cells[0]!.AsObject();
        var crud = plan[IsolatedPlanFields.Matrices]![IsolatedPlanFields.Crud]![IsolatedPlanFields.Include]!.AsArray();
        Action mutation = corruption switch
        {
            "empty" => cells.Clear,
            "missing" => () => cells.RemoveAt(0),
            "duplicate" => () => cells[1] = first.DeepClone(),
            "unknown-target" => () => first[IsolatedPlanFields.Target] = "Unowned engine",
            "zero-nodes" => () => first[IsolatedPlanFields.NodeCount] = 0,
            "two-nodes" => () => first[IsolatedPlanFields.NodeCount] = 2,
            "four-nodes" => () => first[IsolatedPlanFields.NodeCount] = 4,
            "text-nodes" => () => first[IsolatedPlanFields.NodeCount] = "3",
            "unknown-scenario" => () => first[IsolatedPlanFields.Scenario] = "PretendMeasured",
            "foreign-profile" => () => first[IsolatedPlanFields.Profile] = "light",
            "unsafe-id" => () => first[IsolatedPlanFields.Id] = "../../private",
            "native-label" => () => first[IsolatedPlanFields.Native] = true,
            "extra-plan-field" => () => plan[IsolatedPlanFields.Measured] = true,
            "options-drift" => () => plan[IsolatedPlanFields.Options]![IsolatedPlanFields.Operations] = 1,
            "missing-matrix" => () => crud.RemoveAt(0),
            "duplicate-matrix" => () => crud[1] = crud[0]!.DeepClone(),
            "foreign-matrix" => () => crud[0]![IsolatedPlanFields.Target] = "Unowned engine",
            "wrong-family" => () => first[IsolatedPlanFields.Family] = "specialized",
            "schema-drift" => () => plan[IsolatedPlanFields.WorkerSchemaVersion] = 3,
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        mutation();
    }

    private static void CorruptContract(JsonObject contract, string corruption)
    {
        Action mutation = corruption switch
        {
            "empty-targets" => () => contract[IsolatedPlanFields.Targets]!.AsArray().Clear(),
            "unknown-target" => () => contract[IsolatedPlanFields.Targets]![0] = "Unowned engine",
            "duplicate-target" => () => contract[IsolatedPlanFields.Targets]![1] = contract[IsolatedPlanFields.Targets]![0]!.DeepClone(),
            "missing-nodes" => () => contract[IsolatedPlanFields.NodeCounts]!.AsArray().RemoveAt(1),
            "fake-nodes" => () => contract[IsolatedPlanFields.NodeCounts]![1] = 30,
            "two-nodes" => () => contract[IsolatedPlanFields.NodeCounts]!.AsArray().Insert(1, JsonValue.Create(2)),
            "duplicate-scenario" => () => contract[IsolatedPlanFields.SpecializedScenarios]![0] = contract[IsolatedPlanFields.CrudScenarios]![0]!.DeepClone(),
            "unknown-scenario" => () => contract[IsolatedPlanFields.CrudScenarios]![0] = "PretendMeasured",
            "missing-options" => () => contract[IsolatedPlanFields.Options]!.AsObject().Remove(IsolatedPlanFields.Operations),
            "changed-options" => () => contract[IsolatedPlanFields.Options]![IsolatedPlanFields.Operations] = 1,
            "extra-options" => () => contract[IsolatedPlanFields.Options]![IsolatedPlanFields.Native] = true,
            "native-label" => () => contract[IsolatedPlanFields.UnsupportedTopologies]![0]![IsolatedPlanFields.Native] = true,
            "profile-drift" => () => contract[IsolatedPlanFields.Profile] = "light",
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        mutation();
    }
}
