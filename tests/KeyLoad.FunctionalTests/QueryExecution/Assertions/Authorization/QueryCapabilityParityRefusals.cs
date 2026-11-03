using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.FunctionalTests.GraphTraversal;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryCapabilityParityRefusals
{
    internal static Task RunAsync(QueryCapabilityParityFixture fixture, DatabaseEngine database, ZoneTreeStore store,
        bool denied, CancellationToken token) => GraphLabelUseOperations.ObserveAsync(store, async () =>
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var request = QueryCapabilityParityRequests.Ast(fixture.Harness.Partition);
        var code = denied ? ErrorCode.PermissionDenied : ErrorCode.UnsupportedCapability;
        var detail = denied ? "The principal cannot perform this operation in this scope." : "The query comparison is unsupported.";
        var ast = denied ? request : request with
        {
            Query = request.Query with
            {
                Filter = new Comparison(new FieldOperand("/score"), "LIKE", ValueOperand.Create(20m))
            }
        };
        QueryPage? partial = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = engine.ExecuteAst(QueryCapabilityParityRequests.Reader, ast, cancellationToken: token));
        await Assert.That(partial).IsNull();
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(failure.Message).IsEqualTo(detail);
        await RequiredProcessingPolicyAssertions.EqualAsync(QueryCapabilityParityExpected.Manifest(), engine.Capabilities);
    });
}
