using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkPendingRf3Continuation
{
    internal static async Task ResumeAsync(TwoRf3MembershipWave wave, SampleChunkPendingRf3State state,
        CancellationToken token)
    {
        await using var administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node1, wave.Profile.AdminKey, token).ConfigureAwait(false);
        await using var creator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node2, state.Scope.Secret, token).ConfigureAwait(false);
        // The new AppHost's original live quota output proves the durable admission survived cold;
        // no probe arm beyond the original strict32 is created and no scheduling retry is requested.
        await SampleChunkPendingRf3Log.RequireAsync(wave, state.Candidate.CommandId, token).ConfigureAwait(false);
        foreach (var item in state.Admitted)
        { await SampleChunkPendingRf3Assertions.PendingAsync(item, creator.Sdk, token).ConfigureAwait(false); }
        await SampleChunkPendingRf3Assertions.CompleteAsync(state.Candidate, creator.Sdk, creator.Mcp, merged: false, token)
            .ConfigureAwait(false);
        await MergeAsync(state.Admitted[SampleChunkPendingRf3Protocol.FirstIndex], administrator, token).ConfigureAwait(false);
        await SampleChunkPendingRf3Assertions.WaitMergedAsync(state.Candidate, creator.Sdk, token).ConfigureAwait(false);
        foreach (var item in state.Admitted.Skip(SampleChunkPendingRf3Protocol.AfterFirstSettled))
        { await MergeAsync(item, administrator, token).ConfigureAwait(false); }
        var fresh = await SampleChunkPendingRf3Seed.CreateAsync(state.Scope, SampleChunkPendingRf3Protocol.FreshOrdinal,
            creator.Sdk, administrator.Sdk, token).ConfigureAwait(false);
        await SampleChunkPendingRf3Seed.CorrectAsync(fresh, administrator.Sdk, token).ConfigureAwait(false);
        await SampleChunkPendingRf3Assertions.WaitMergedAsync(fresh, creator.Sdk, token).ConfigureAwait(false);
        state.Fresh = fresh;
        await AllHealthyAsync(state, creator, token).ConfigureAwait(false);
    }

    internal static async Task AllHealthyAsync(SampleChunkPendingRf3State state, RequestCqrsRf3Callers caller,
        CancellationToken token)
    {
        foreach (var item in state.Admitted)
        { await SampleChunkPendingRf3Assertions.CompleteAsync(item, caller.Sdk, caller.Mcp, merged: true, token).ConfigureAwait(false); }
        await SampleChunkPendingRf3Assertions.CompleteAsync(state.Candidate, caller.Sdk, caller.Mcp, merged: true, token).ConfigureAwait(false);
        await SampleChunkPendingRf3Assertions.CompleteAsync(state.Fresh
            ?? throw new InvalidOperationException(SampleChunkPendingRf3Protocol.Missing), caller.Sdk, caller.Mcp, merged: true, token)
            .ConfigureAwait(false);
    }

    private static async Task MergeAsync(SampleChunkPendingRf3Item item, RequestCqrsRf3Callers administrator,
        CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), item.Partition,
            [new MergeSampleChunkWindow(TimeSeriesRf3Scenario.Set, item.Series, item.WindowId,
                SampleChunkRf3Protocol.CorrectedRevision)]);
        await SampleChunkRetentionRf3WriteOracle.CommitAsync(administrator.Sdk, administrator.Mcp, request,
            [new(SampleChunkProtocol.MergeKind, TimeSeriesRf3Scenario.Set, item.Series, SampleChunkRf3Protocol.MergedRevision)],
            official: true, token).ConfigureAwait(false);
    }
}
