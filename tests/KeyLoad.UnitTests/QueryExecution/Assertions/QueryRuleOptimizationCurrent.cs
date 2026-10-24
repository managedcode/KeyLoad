using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Authorization;
using KeyLoad.UnitTests.GraphTraversal;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryRuleOptimizationCurrent
{
    internal static async Task CheckAsync(QueryRuleOptimizationFixture fixture, DatabaseEngine owner, ZoneTreeStore store)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(fixture, owner), failures);
        foreach (var original in fixture.Originals)
        {
            await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ReplayAsync(owner, store,
                original.Command, original.Receipt, original.Outcome), failures);
            await ServerFailureObserver.ObserveAsync(() => ImageAsync(fixture, owner), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ImageAsync(QueryRuleOptimizationFixture fixture, DatabaseEngine owner)
    {
        foreach (var collection in QueryRuleOptimizationData.Collections)
        {
            foreach (var row in QueryRuleOptimizationData.Rows)
            {
                var reference = fixture.Harness.ExpectedReference(new(fixture.Harness.Partition, collection, row.Id));
                await RequiredProcessingPolicyAssertions.EqualAsync(new DocumentResult(reference, row.Revision, row.Json, false, []),
                    owner.GetDocument("root", reference) ?? throw new InvalidOperationException("The original optimizer row is absent."));
            }
        }
    }
}
