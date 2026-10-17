using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockRf3Policy
{
    private const long DeniedEpoch = 2;
    private const long RepairedEpoch = 3;
    private const string ScopeDenied = "The principal cannot perform this operation in this scope.";

    internal static async Task RunAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        ChunkMultiblockRf3State state, CancellationToken token)
    {
        var denied = state.Reader.Principal with
        {
            PolicyEpoch = DeniedEpoch,
            Grants = []
        };
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)), denied);
        await ChunkMultiblockRf3Current.RequireAsync(administrator, administrator, state, token);
        await DeniedSdkAsync(administrator, reader, state, token);
        await DeniedMcpAsync(administrator, reader, state, token);
        var repaired = state.Reader.Principal with
        {
            PolicyEpoch = RepairedEpoch
        };
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)), repaired);
        await ChunkMultiblockRf3Current.RequireAsync(administrator, reader, state, token);
        await ChunkMultiblockRf3Writes.ReplayAsync(administrator, state, token);
    }

    private static async Task DeniedSdkAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        ChunkMultiblockRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = await reader.Sdk.ReadSampleChunkWindowAsync(ChunkMultiblockRf3Pages.Request(state), token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Problem).IsNotNull();
            await SampleChunkRetentionRf3ReadOracle.EqualAsync(result.Problem!, Errors.Problem(ErrorCode.PermissionDenied, ScopeDenied));
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkMultiblockRf3Current.RequireAsync(administrator, administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DeniedMcpAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        ChunkMultiblockRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = await reader.Mcp.CallAsync(SampleChunkProtocol.ReadTool, ChunkMultiblockRf3Pages.Request(state), token);
            await McpCallerAssertions.ErrorAsync(result, ErrorCode.PermissionDenied, dispatched: true);
            await Assert.That(result.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(ScopeDenied);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkMultiblockRf3Current.RequireAsync(administrator, administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
