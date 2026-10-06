using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedMutationOracleTests
{
    [Test]
    public void AcIso005UpdateRequiresTheExactFinalBodyAndDeleteRequiresAbsence()
    {
        var data = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small));
        var input = data.Input(Scenario.DocumentUpdate, 0, 0, false);
        var initial = BenchmarkDataset.InitialMutationState(Scenario.DocumentUpdate, input);
        ComparisonMutationOracle.RequireFinalState(Scenario.DocumentUpdate, new(input.Id, input.Json), input);
        ComparisonMutationOracle.RequireFinalState(Scenario.DocumentDelete, null, input);
        Assert.ThrowsExactly<ComparisonFailureException>(() =>
            ComparisonMutationOracle.RequireFinalState(Scenario.DocumentUpdate, new(initial.Id, initial.Json), input));
        Assert.ThrowsExactly<ComparisonFailureException>(() =>
            ComparisonMutationOracle.RequireFinalState(Scenario.DocumentUpdate, null, input));
        Assert.ThrowsExactly<ComparisonFailureException>(() =>
            ComparisonMutationOracle.RequireFinalState(Scenario.DocumentDelete, new(input.Id, input.Json), input));
        Assert.ThrowsExactly<ComparisonFailureException>(() =>
            ComparisonMutationOracle.RequireFinalState(Scenario.DocumentUpdate, new(data.Documents[0].Id, input.Json), input));
    }

    [Test]
    public void AcIso005MutationOracleRejectsAnUnrelatedOperation()
    {
        var input = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small)).Documents[0];
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ComparisonMutationOracle.RequireFinalState(Scenario.PointRead, null, input));
    }
}
