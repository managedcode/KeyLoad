using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class ChunkMultiblockClusterRefusal
{
    private const int FirstIndex = 0;
    private const double ChangedValue = 8;

    internal static async Task RunAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        ChunkMultiblockClusterState state, CancellationToken token)
    {
        state.Conflict ??= new(Guid.NewGuid(), state.Scenario.Execution.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                [ChunkMultiblockClusterData.Original[FirstIndex] with { Value = ChangedValue }], TimeSeriesClusterScenario.PublicTags)]);
        var result = await administrator.Sdk.CommitAsync(state.Conflict, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SampleChunkRetentionClusterReadOracle.EqualAsync(result.Problem!, Errors.Problem(ErrorCode.Conflict, SeriesRetentionAtomicPublicExpected.ConflictDetail));
        await ChunkMultiblockClusterCurrent.RequireAsync(administrator, reader, state, token);
        var official = await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Conflict, token);
        _ = (await McpCallerAssertions.ErrorAsync(official, ErrorCode.Conflict, dispatched: true)).Value;
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(SeriesRetentionAtomicPublicExpected.McpDetail);
        await ChunkMultiblockClusterCurrent.RequireAsync(administrator, reader, state, token);
        await ChunkMultiblockClusterWrites.ReplayAsync(administrator, state, token);
    }
}
