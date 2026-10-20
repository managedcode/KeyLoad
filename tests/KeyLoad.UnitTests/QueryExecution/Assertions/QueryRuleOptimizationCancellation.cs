using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryRuleOptimizationCancellation
{
    internal static Task RunAsync(QueryRuleOptimizationFixture fixture, DatabaseEngine owner, ZoneTreeStore store)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            foreach (var execute in new Func<QueryPage>[]
            {
                () => engine.ExecuteAst(QueryRuleOptimizationData.Reader,
                    QueryRuleOptimizationRequests.Ast(fixture.Harness.Partition, QueryRuleOptimizationData.RevisionOnlyCollection, QueryRuleOptimizationData.OpenRevisionOneShape, false), cancellationToken: cancellation.Token),
                () => engine.Execute(QueryRuleOptimizationData.Reader,
                    QueryRuleOptimizationRequests.Sql(fixture.Harness.Partition, QueryRuleOptimizationData.RevisionOnlyCollection, QueryRuleOptimizationData.OpenRevisionOneShape, false), cancellationToken: cancellation.Token)
            })
            {
                var failure = Assert.ThrowsExactly<OperationCanceledException>(() => execute());
                await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
            }
        });
}
