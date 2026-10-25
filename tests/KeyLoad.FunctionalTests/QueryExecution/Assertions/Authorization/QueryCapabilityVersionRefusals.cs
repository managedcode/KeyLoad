using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.FunctionalTests.GraphTraversal;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryCapabilityVersionRefusals
{
    internal static Task RunAsync(QueryCapabilityVersionFixture fixture, DatabaseEngine database, ZoneTreeStore store,
        bool denied, CancellationToken token) => GraphLabelUseOperations.ObserveAsync(store, async () =>
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var request = QueryCapabilityVersionRequests.Ast(fixture.Harness.Partition, 1);
        var code = denied ? ErrorCode.PermissionDenied : ErrorCode.UnsupportedCapability;
        var detail = denied ? "The principal cannot perform this operation in this scope." : "The query AST version is unsupported.";
        var ast = denied ? request : request with
        {
            AstVersion = 3
        };
        QueryPage? partial = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = engine.ExecuteAst(QueryCapabilityVersionRequests.Reader, ast, cancellationToken: token));
        await Assert.That(partial).IsNull();
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(failure.Message).IsEqualTo(detail);
        if (!denied)
        {
            var unknown = request with
            {
                Query = request.Query with
                {
                    Filter = new Comparison(new FieldOperand("/score"), "LIKE", ValueOperand.Create(20m))
                }
            };
            var unsupported = Assert.ThrowsExactly<KeyLoadException>(() => partial = engine.ExecuteAst(QueryCapabilityVersionRequests.Reader, unknown, cancellationToken: token));
            await Assert.That(partial).IsNull();
            await Assert.That(unsupported.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            await Assert.That(unsupported.Message).IsEqualTo("The query comparison is unsupported.");
        }
        await RequiredProcessingPolicyAssertions.EqualAsync(QueryCapabilityVersionExpected.Manifest(), engine.Capabilities);
    });
}
