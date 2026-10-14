using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesResourceChunkPolicyRf3Enrollment
{
    private const long OpenRevision = 1;
    private const long BeforeSealRevision = 3;
    private const long SealedRevision = 4;
    private const long NoPosition = 0;

    internal static async Task<(Guid WindowId, CommandRequest Request, CommitReceipt Receipt)> OpenAsync(
        RequestCqrsRf3Callers administrator, PartitionRef partition, CancellationToken token)
    {
        var windowId = Guid.NewGuid();
        var request = new CommandRequest(Guid.NewGuid(), partition,
            [new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                windowId, TimeSeriesRf3Scenario.Start, DateTimeOffset.MaxValue)]);
        var receipt = await SampleChunkRetentionRf3WriteOracle.CommitAsync(administrator.Sdk, administrator.Mcp,
            request, [new MutationReceipt(SampleChunkProtocol.OpenKind, TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, OpenRevision)], official: false, token);
        await Assert.That(receipt.Token.Position).IsGreaterThan(NoPosition);
        return (windowId, request, receipt);
    }

    internal static async Task<(CommandRequest Request, CommitReceipt Receipt)> SealAsync(
        RequestCqrsRf3Callers administrator, PartitionRef partition, Guid windowId, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), partition,
            [new SealSampleChunkWindow(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                windowId, BeforeSealRevision)]);
        var receipt = await SampleChunkRetentionRf3WriteOracle.CommitAsync(administrator.Sdk, administrator.Mcp,
            request, [new MutationReceipt(SampleChunkProtocol.SealKind, TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, SealedRevision)], official: true, token);
        await Assert.That(receipt.Token.Position).IsGreaterThan(NoPosition);
        return (request, receipt);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        foreach (var original in state.ChunkCommands)
        {
            foreach (var official in new[] { false, true })
            {
                await TimeSeriesResourceChunkPolicyRf3Read.ObserveAsync(async () =>
                {
                    var actual = official
                        ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await administrator.Mcp.CallAsync(
                            McpCallerTools.DocumentsCommit, original.Request, token))).Value
                        : await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original.Request, token));
                    await SqlRf3Protocol.EqualAsync(original.Receipt, actual);
                }, administrator, state, token);
            }
        }
        await TimeSeriesResourceChunkPolicyRf3Read.RunAsync(reader, administrator, state, token);
    }
}
