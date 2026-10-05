using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-002/007: actual Node produces the complete canonical isolated cohort.</summary>
internal sealed class IsolatedPlanTests
{
    [Test]
    public async Task AcIso002PlanCoversEveryCanonicalCellExactlyOnce()
    {
        var response = await IsolatedPlanNodeProcess.ProbeAsync("create");
        await Assert.That(response[IsolatedPlanFields.Ok]!.GetValue<bool>()).IsTrue();
        var plan = response[IsolatedPlanFields.Value]!.AsObject();
        var contract = JsonNode.Parse(await File.ReadAllTextAsync(IsolatedPlanNodeProcess.ContractPath,
            TestContext.Current!.Execution.CancellationToken))!.AsObject();
        await Assert.That(plan[IsolatedPlanFields.SchemaVersion]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(plan[IsolatedPlanFields.WorkerSchemaVersion]!.GetValue<int>()).IsEqualTo(5);
        await Assert.That(JsonNode.DeepEquals(plan[IsolatedPlanFields.Options], contract[IsolatedPlanFields.Options])).IsTrue();
        await Assert.That(plan[IsolatedPlanFields.Profile]!.GetValue<string>()).IsEqualTo(contract[IsolatedPlanFields.Profile]!.GetValue<string>());
        var cells = plan[IsolatedPlanFields.Cells]!.AsArray();
        await Assert.That(cells.Count).IsEqualTo(330);
        await Assert.That(cells.Select(cell => cell![IsolatedPlanFields.Id]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(330);
        await VerifyDimensionsAsync(cells, contract);
        var validated = await IsolatedPlanNodeProcess.ProbeAsync("validate", plan);
        await Assert.That(validated[IsolatedPlanFields.Ok]!.GetValue<bool>()).IsTrue();
        await Assert.That(JsonNode.DeepEquals(validated[IsolatedPlanFields.Value], plan)).IsTrue();
    }

    [Test]
    public async Task AcIso002MatricesAreDisjointCompleteAndBelowGithubLimit()
    {
        var plan = (await IsolatedPlanNodeProcess.ProbeAsync("create"))[IsolatedPlanFields.Value]!.AsObject();
        var matrices = plan[IsolatedPlanFields.Matrices]!;
        var crud = matrices[IsolatedPlanFields.Crud]![IsolatedPlanFields.Include]!.AsArray();
        var specialized = matrices[IsolatedPlanFields.Specialized]![IsolatedPlanFields.Include]!.AsArray();
        await Assert.That(crud.Count).IsEqualTo(108);
        await Assert.That(specialized.Count).IsEqualTo(162);
        await Assert.That(crud.Count < 256 && specialized.Count < 256).IsTrue();
        var all = crud.Concat(specialized).Select(cell => cell![IsolatedPlanFields.Id]!.GetValue<string>()).ToArray();
        await Assert.That(all.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(330);
        await Assert.That(all.Order(StringComparer.Ordinal)).IsEquivalentTo(
            plan[IsolatedPlanFields.Cells]!.AsArray().Select(cell => cell![IsolatedPlanFields.Id]!.GetValue<string>()).Order(StringComparer.Ordinal),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(crud.All(cell => cell![IsolatedPlanFields.Family]!.GetValue<string>() == "crud")).IsTrue();
        await Assert.That(specialized.All(cell => cell![IsolatedPlanFields.Family]!.GetValue<string>() == "specialized")).IsTrue();
    }

    [Test]
    public async Task AcIso002SafeIdsAreStableAndUnsupportedNativeTopologiesRemainInThePlan()
    {
        var first = (await IsolatedPlanNodeProcess.ProbeAsync("create"))[IsolatedPlanFields.Value]!;
        var second = (await IsolatedPlanNodeProcess.ProbeAsync("create"))[IsolatedPlanFields.Value]!;
        await Assert.That(JsonNode.DeepEquals(first, second)).IsTrue();
        var cells = first[IsolatedPlanFields.Cells]!.AsArray();
        await Assert.That(cells.Single(cell => cell![IsolatedPlanFields.Target]!.GetValue<string>() == "KeyLoad"
            && cell[IsolatedPlanFields.NodeCount]!.GetValue<int>() == 1 && cell[IsolatedPlanFields.Scenario]!.GetValue<string>() == "PointRead")![IsolatedPlanFields.Id]!
            .GetValue<string>()).IsEqualTo("keyload-n1-point-read");
        await Assert.That(cells.Single(cell => cell![IsolatedPlanFields.Target]!.GetValue<string>() == "PostgreSQL + pgvector"
            && cell[IsolatedPlanFields.NodeCount]!.GetValue<int>() == 2 && cell[IsolatedPlanFields.Scenario]!.GetValue<string>() == "DocumentUpdate")![IsolatedPlanFields.Id]!
            .GetValue<string>()).IsEqualTo("postgresql-pgvector-n2-document-update");
        await Assert.That(cells.All(cell => IsSafeId(cell![IsolatedPlanFields.Id]!.GetValue<string>()))).IsTrue();
        await Assert.That(cells.Count(cell => cell![IsolatedPlanFields.Target]!.GetValue<string>() == "Neo4j"
            && cell[IsolatedPlanFields.NodeCount]!.GetValue<int>() > 1)).IsEqualTo(20);
        await Assert.That(cells.All(cell => cell!.AsObject().Count == 6)).IsTrue();
    }

    private static async Task VerifyDimensionsAsync(JsonArray cells, JsonObject contract)
    {
        var scenarios = contract[IsolatedPlanFields.CrudScenarios]!.AsArray().Concat(contract[IsolatedPlanFields.SpecializedScenarios]!.AsArray());
        foreach (var target in contract[IsolatedPlanFields.Targets]!.AsArray())
        {
            foreach (var nodes in contract[IsolatedPlanFields.NodeCounts]!.AsArray())
            {
                foreach (var scenario in scenarios)
                {
                    await Assert.That(cells.Count(cell => JsonNode.DeepEquals(cell![IsolatedPlanFields.Target], target)
                        && JsonNode.DeepEquals(cell[IsolatedPlanFields.NodeCount], nodes) && JsonNode.DeepEquals(cell[IsolatedPlanFields.Scenario], scenario)))
                        .IsEqualTo(1);
                }
            }
        }
    }

    private static bool IsSafeId(string id) => id.Length is > 0 and <= 120
        && id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        && !id.StartsWith('-') && !id.EndsWith('-') && !id.Contains("--", StringComparison.Ordinal);
}
