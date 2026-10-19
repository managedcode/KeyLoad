using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesResourceChunkPolicyClusterEnrollment
{
    private const long OpenRevision = 1;
    private const long BeforeSealRevision = 3;
    private const long SealedRevision = 4;
    private const long NoPosition = 0;

    internal static async Task<(Guid WindowId, CommandRequest Request, CommitReceipt Receipt)> OpenAsync(
        RequestCqrsClusterCallers administrator, PartitionRef partition, CancellationToken token)
    {
        var windowId = Guid.NewGuid();
        var request = new CommandRequest(Guid.NewGuid(), partition,
            [new OpenSampleChunkWindow(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                windowId, TimeSeriesClusterScenario.Start, DateTimeOffset.MaxValue)]);
        var receipt = await SampleChunkRetentionClusterWriteOracle.CommitAsync(administrator.Sdk, administrator.Mcp,
            request, [new MutationReceipt(SampleChunkProtocol.OpenKind, TimeSeriesClusterScenario.Set,
                TimeSeriesClusterScenario.Series, OpenRevision)], official: false, token);
        await Assert.That(receipt.Token.Position).IsGreaterThan(NoPosition);
        return (windowId, request, receipt);
    }

    internal static async Task<(CommandRequest Request, CommitReceipt Receipt)> SealAsync(
        RequestCqrsClusterCallers administrator, PartitionRef partition, Guid windowId, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), partition,
            [new SealSampleChunkWindow(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                windowId, BeforeSealRevision)]);
        var receipt = await SampleChunkRetentionClusterWriteOracle.CommitAsync(administrator.Sdk, administrator.Mcp,
            request, [new MutationReceipt(SampleChunkProtocol.SealKind, TimeSeriesClusterScenario.Set,
                TimeSeriesClusterScenario.Series, SealedRevision)], official: true, token);
        await Assert.That(receipt.Token.Position).IsGreaterThan(NoPosition);
        return (request, receipt);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        TimeSeriesResourceTagPolicyClusterState state, CancellationToken token)
    {
        foreach (var original in state.ChunkCommands)
        {
            foreach (var official in new[] { false, true })
            {
                await TimeSeriesResourceChunkPolicyClusterRead.ObserveAsync(async () =>
                {
                    var actual = official
                        ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await administrator.Mcp.CallAsync(
                            McpCallerTools.DocumentsCommit, original.Request, token))).Value
                        : await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original.Request, token));
                    await SqlClusterProtocol.EqualAsync(original.Receipt, actual);
                }, administrator, state, token);
            }
        }
        await TimeSeriesResourceChunkPolicyClusterRead.RunAsync(reader, administrator, state, token);
    }
}
