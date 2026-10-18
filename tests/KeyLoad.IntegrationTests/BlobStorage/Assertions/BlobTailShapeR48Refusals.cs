using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobTailShapeR48Refusals
{
    private const string InvalidDetail = "The blob request or policy is invalid.";
    private const string SafeDetail = "The database operation could not be completed.";

    internal static async Task BothAsync(RequestCqrsClusterCallers callers,
        BlobTailShapeR48State state, CancellationToken token)
    {
        foreach (var request in state.Requests)
        {
            var result = await callers.Sdk.WriteBlobPartAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Validation, InvalidDetail), result.Problem!);
            await BlobPartIdentityR40Assertions.CurrentAsync(callers, state.Original, token);
            var reply = await callers.Mcp.CallAsync(BlobPartIdentityR40Replay.PartTool, request, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true);
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(SafeDetail);
            await BlobPartIdentityR40Assertions.CurrentAsync(callers, state.Original, token);
        }
    }
}
