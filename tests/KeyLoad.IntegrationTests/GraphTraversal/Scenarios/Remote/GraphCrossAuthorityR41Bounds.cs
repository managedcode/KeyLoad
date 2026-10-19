using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphCrossAuthorityR41Bounds
{
    internal static async Task VerifyAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        GraphCrossAuthorityR41State state, CancellationToken token)
    {
        await RefuseAsync(reader, administrator, state, state.Request(2, 1, true), token);
        await RefuseAsync(reader, administrator, state, state.Request(3, 1), token);
        await RefuseAsync(reader, administrator, state, state.Request(state.Hidden ? 1 : 2, 2), token);
        await GraphCrossAuthorityR41Assertions.MatrixAsync(reader, administrator, state, token);
    }

    private static async Task RefuseAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        GraphCrossAuthorityR41State state, TraverseRequest request, CancellationToken token)
    {
        var sdk = await reader.Sdk.TraverseAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
        await GraphCrossAuthorityR41Assertions.MatrixAsync(reader, administrator, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.BudgetExceeded, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Seed.Reader.Secret, GraphIncomingClusterScenario.PrivateMarker);
        await GraphCrossAuthorityR41Assertions.MatrixAsync(reader, administrator, state, token);
    }

    internal static async Task OriginalsAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        GraphCrossAuthorityR41State state, CancellationToken token)
    {
        foreach (var original in state.Originals)
        {
            await SqlClusterProtocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.CommitAsync(original.Command, token)));
            await GraphCrossAuthorityR41Assertions.MatrixAsync(reader, administrator, state, token);
            await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
            await GraphCrossAuthorityR41Assertions.MatrixAsync(reader, administrator, state, token);
        }
    }
}
