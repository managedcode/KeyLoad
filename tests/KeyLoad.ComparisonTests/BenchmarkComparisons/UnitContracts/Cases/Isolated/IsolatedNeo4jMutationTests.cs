using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNeo4jMutationTests
{
    private const string One = "[[1]]";
    private const string Zero = "[[0]]";
    private const string Two = "[[2]]";
    private const string Duplicate = "[[1],[1]]";

    [Test]
    public void AcIso005NativeNeo4jMutationCountMustBeExactlyOne()
    {
        using var one = JsonDocument.Parse(One);
        using var zero = JsonDocument.Parse(Zero);
        using var two = JsonDocument.Parse(Two);
        using var duplicate = JsonDocument.Parse(Duplicate);
        Neo4jMutationOperations.RequireAffected(one.RootElement, Scenario.DocumentUpdate);
        Assert.ThrowsExactly<ComparisonFailureException>(() =>
            Neo4jMutationOperations.RequireAffected(zero.RootElement, Scenario.DocumentUpdate));
        Assert.ThrowsExactly<ComparisonFailureException>(() =>
            Neo4jMutationOperations.RequireAffected(zero.RootElement, Scenario.DocumentDelete));
        Assert.ThrowsExactly<ComparisonFailureException>(() =>
            Neo4jMutationOperations.RequireAffected(two.RootElement, Scenario.DocumentUpdate));
        Assert.ThrowsExactly<ComparisonFailureException>(() =>
            Neo4jMutationOperations.RequireAffected(duplicate.RootElement, Scenario.DocumentDelete));
    }
}
