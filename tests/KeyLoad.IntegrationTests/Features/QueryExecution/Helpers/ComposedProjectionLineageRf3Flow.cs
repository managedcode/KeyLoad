using KeyLoad.IntegrationTests.Features.DatabaseComposition;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ComposedProjectionLineageRf3Flow
{
    private const string OriginalSelection = "d.name AS customer_name";
    private const string ExpandedSelection = "q.payload.to.id AS reference_id, d.name AS customer_name";
    private const string LeftPredicate = "q.payload.score >= @minimum";
    private const string RightPredicate = "d.name = 'Ada'";

    internal static async Task VerifyAsync(ClusterFixture fixture, QueueReferenceSelectState state,
        bool denied, bool masked, CancellationToken token, bool scoreMasked = false)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async callers =>
        {
            var request = QueueReferenceSelectProtocol.Operation(state.Partition);
            request = request with { Sql = request.Sql.Replace(OriginalSelection, ExpandedSelection, StringComparison.Ordinal) };
            if (denied)
            {
                await FailureAsync(callers, request, ErrorCode.PermissionDenied, token);
            }
            else
            {
                var sdk = await SqlRf3Protocol.SdkAsync<QueryPage>(callers.Sdk, request, token);
                var mcp = await SqlRf3Protocol.McpAsync<QueryPage>(callers.Mcp, request, token);
                await ComposedProjectionLineageRf3Assertions.PageAsync(sdk, masked, state.Repair is not null, scoreMasked);
                await ComposedProjectionLineageRf3Assertions.PageAsync(mcp, masked, state.Repair is not null, scoreMasked);
                await Assert.That(mcp.CutPosition).IsGreaterThanOrEqualTo(sdk.CutPosition);
                await SqlRf3Protocol.EqualAsync(sdk with { CutPosition = mcp.CutPosition }, mcp);
                await FailureAsync(callers, request with
                {
                    Sql = request.Sql.Replace(LeftPredicate, RightPredicate,
                    StringComparison.Ordinal)
                }, ErrorCode.UnsupportedCapability, token);
            }
            return true;
        }, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => { await QueueReferenceSelectSources.VerifyAsync(admin, state, token); return true; }, token);
    }

    private static async Task FailureAsync(RequestCqrsRf3Callers callers, SqlOperationRequest request,
        ErrorCode code, CancellationToken token)
    {
        var actual = await callers.Sdk.ExecuteSqlAsync(request, token);
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token),
            code, dispatched: true);
    }
}
