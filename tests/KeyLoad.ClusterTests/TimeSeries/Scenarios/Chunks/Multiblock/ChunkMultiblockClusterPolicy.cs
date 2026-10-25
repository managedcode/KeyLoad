using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class ChunkMultiblockClusterPolicy
{
    private const long DeniedEpoch = 2;
    private const long RepairedEpoch = 3;
    private const string ScopeDenied = "The principal cannot perform this operation in this scope.";

    internal static async Task RunAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        ChunkMultiblockClusterState state, CancellationToken token)
    {
        var denied = state.Reader.Principal with
        {
            PolicyEpoch = DeniedEpoch,
            Grants = []
        };
        await SampleChunkRetentionClusterReadOracle.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)), denied);
        await ChunkMultiblockClusterCurrent.RequireAsync(administrator, administrator, state, token);
        await DeniedSdkAsync(administrator, reader, state, token);
        await DeniedMcpAsync(administrator, reader, state, token);
        var repaired = state.Reader.Principal with
        {
            PolicyEpoch = RepairedEpoch
        };
        await SampleChunkRetentionClusterReadOracle.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)), repaired);
        await ChunkMultiblockClusterCurrent.RequireAsync(administrator, reader, state, token);
        await ChunkMultiblockClusterWrites.ReplayAsync(administrator, state, token);
    }

    private static async Task DeniedSdkAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        ChunkMultiblockClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = await reader.Sdk.ReadSampleChunkWindowAsync(ChunkMultiblockClusterPages.Request(state), token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Problem).IsNotNull();
            await SampleChunkRetentionClusterReadOracle.EqualAsync(result.Problem!, Errors.Problem(ErrorCode.PermissionDenied, ScopeDenied));
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkMultiblockClusterCurrent.RequireAsync(administrator, administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DeniedMcpAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        ChunkMultiblockClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = await reader.Mcp.CallAsync(SampleChunkProtocol.ReadTool, ChunkMultiblockClusterPages.Request(state), token);
            _ = (await McpCallerAssertions.ErrorAsync(result, ErrorCode.PermissionDenied, dispatched: true)).Value;
            await Assert.That(result.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(ScopeDenied);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkMultiblockClusterCurrent.RequireAsync(administrator, administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
