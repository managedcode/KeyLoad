using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetainedIdentityClusterAssertions
{
    private const string ConflictDetail = "The native series chunk window overlaps retained data or an enrolled identity.";
    private const string SafeDetail = "The database operation could not be completed.";

    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SampleChunkRetainedIdentityClusterState original, Func<Task> current, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = await sdk.CommitAsync(original.Request, token);
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(actual.Problem, Errors.Problem(ErrorCode.Conflict, ConflictDetail));
        }, failures);
        await PreserveAsync(sdk, mcp, original, current, failures, token);
        ServerFailureObserver.ThrowIfAny(failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = await mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Request, token);
            await McpCallerAssertions.ErrorAsync(actual, ErrorCode.Conflict, dispatched: true);
            await SqlClusterProtocol.EqualAsync<object>(Errors.Problem(ErrorCode.Conflict, SafeDetail),
                actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error));
        }, failures);
        await PreserveAsync(sdk, mcp, original, current, failures, token);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task PreserveAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SampleChunkRetainedIdentityClusterState original, Func<Task> current, List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(current, failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(original.Outbox,
            await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(original.Request.Partition, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(original.Outbox,
            (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(original.Request.Partition), token))).Value), failures);
    }
}
