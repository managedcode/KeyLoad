using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DatabaseComposition;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class ComposedProjectionLineageClusterFlow
{
    private const string OriginalSelection = "d.name AS customer_name";
    private const string ExpandedSelection = "q.payload.[to].id AS reference_id, d.name AS customer_name";
    private const string LeftPredicate = "q.payload.score >= @minimum";
    private const string RightPredicate = "d.name = 'Ada'";

    internal static async Task VerifyAsync(ClusterFixture fixture, QueueReferenceSelectState state,
        bool denied, bool masked, CancellationToken token, bool scoreMasked = false)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async callers =>
        {
            var request = QueueReferenceSelectProtocol.Operation(state.Partition);
            request = request with
            {
                Sql = request.Sql.Replace(OriginalSelection, ExpandedSelection, StringComparison.Ordinal)
            };
            if (denied)
            {
                await FailureAsync(callers, request, ErrorCode.PermissionDenied, dispatched: true, token);
            }
            else
            {
                var sdk = await SqlClusterProtocol.SdkAsync<QueryPage>(callers.Sdk, request, token);
                var mcp = await SqlClusterProtocol.McpAsync<QueryPage>(callers.Mcp, request, token);
                await ComposedProjectionLineageClusterAssertions.PageAsync(sdk, masked, state.Repair is not null, scoreMasked);
                await ComposedProjectionLineageClusterAssertions.PageAsync(mcp, masked, state.Repair is not null, scoreMasked);
                await Assert.That(mcp.CutPosition).IsGreaterThanOrEqualTo(sdk.CutPosition);
                await SqlClusterProtocol.EqualAsync(sdk with
                {
                    CutPosition = mcp.CutPosition
                }, mcp);
                await FailureAsync(callers, request with
                {
                    Sql = request.Sql.Replace(LeftPredicate, RightPredicate,
                    StringComparison.Ordinal)
                }, ErrorCode.UnsupportedCapability, dispatched: false, token);
            }
            return true;
        }, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => { await QueueReferenceSelectSources.VerifyAsync(admin, state, token); return true; }, token);
    }

    private static async Task FailureAsync(RequestCqrsClusterCallers callers, SqlOperationRequest request,
        ErrorCode code, bool dispatched, CancellationToken token)
    {
        var actual = await callers.Sdk.ExecuteSqlAsync(request, token);
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(code.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token),
            code, dispatched)).Value;
    }
}
