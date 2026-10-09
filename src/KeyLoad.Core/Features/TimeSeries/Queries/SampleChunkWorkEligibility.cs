using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWorkEligibility
{
    internal static SampleChunkWorkHint? Read(DatabaseEngine database, IKeyValueView view,
        PartitionRef partition, string set, string series, Guid id, DateTimeOffset now,
        ReadExecutionBudget budget)
    {
        budget.Check();
        var charge = new SampleChunkReadCharge(database.Limits.MaxQueryReadBytes);
        var state = SampleChunkStorage.Read<SampleChunkWindow>(view, SampleChunkKeys.Window(partition, set, series, id), charge);
        if (state is null) { return null; }
        SampleChunkWindowValidation.State(state, id, database.TimeSeriesOptions.Value.MaximumChunkWindowRecords,
            database.TimeSeriesOptions.Value.MaximumChunkCorrections);
        var seal = state.State == SampleChunkWindowState.Open && !state.OpenRecords.IsEmpty && now.UtcTicks >= state.UntilUtcTicks;
        var merge = state.State == SampleChunkWindowState.Sealed && !state.CorrectionSequences.IsEmpty;
        if (!seal && !merge) { return null; }
        var principal = database.Principal(view, state.CreatorPrincipalId, now);
        database.Authorization.Require(principal, partition, set, Capability.SeriesManage | Capability.SeriesRead);
        _ = database.Resource(view, partition, set, ResourceKind.TimeSeries);
        return new(partition, set, series, id, state.Generation, state.Revision, state.CreatorPrincipalId, seal,
            SampleChunkJobIdentity.CommandId(partition, set, series, id, state.Generation, state.Revision, seal,
                principal.PolicyEpoch), principal.PolicyEpoch);
    }

    internal static bool SameOriginal(SampleChunkWorkHint left, SampleChunkWorkHint right)
        => left.Partition == right.Partition && left.Set == right.Set && left.Series == right.Series
            && left.WindowId == right.WindowId && left.Generation == right.Generation && left.Revision == right.Revision
            && left.Creator == right.Creator && left.Seal == right.Seal && left.CommandId == right.CommandId
            && left.CreatorPolicyEpoch == right.CreatorPolicyEpoch;
}
