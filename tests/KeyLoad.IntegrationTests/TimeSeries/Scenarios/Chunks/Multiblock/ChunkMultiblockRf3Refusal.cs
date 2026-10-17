using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockRf3Refusal
{
    private const int FirstIndex = 0;
    private const double ChangedValue = 8;

    internal static async Task RunAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        ChunkMultiblockRf3State state, CancellationToken token)
    {
        state.Conflict ??= new(Guid.NewGuid(), state.Scenario.Native.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                [ChunkMultiblockRf3Data.Original[FirstIndex] with { Value = ChangedValue }], TimeSeriesRf3Scenario.PublicTags)]);
        var result = await administrator.Sdk.CommitAsync(state.Conflict, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(result.Problem!, Errors.Problem(ErrorCode.Conflict, SeriesRetentionAtomicPublicExpected.ConflictDetail));
        await ChunkMultiblockRf3Current.RequireAsync(administrator, reader, state, token);
        var official = await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Conflict, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.Conflict, dispatched: true);
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(SeriesRetentionAtomicPublicExpected.McpDetail);
        await ChunkMultiblockRf3Current.RequireAsync(administrator, reader, state, token);
        await ChunkMultiblockRf3Writes.ReplayAsync(administrator, state, token);
    }
}
