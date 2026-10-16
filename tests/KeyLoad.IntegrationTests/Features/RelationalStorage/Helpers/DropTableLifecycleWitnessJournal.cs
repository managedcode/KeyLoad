using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleWitnessJournal
{
    internal static DropLifecycleRf3ReceiptProof? Read(ZoneTreeStore replica, string replicaDirectory, DatabaseEngine database,
        IKeyValueView canonical, PhysicalShardRecord owner, string voter, PhysicalShardCatalog catalog,
        ResourceRetirementFinalizationV1 history, ZoneTreeConservationBudget work, CancellationToken token)
    {
        var configuration = new ReplicaConfiguration(voter, owner.VoterIds, replicaDirectory,
            owner.Incarnation);
        configuration.Validate();
        var entry = replica.Read(source =>
        {
            var view = new ZoneTreeConservationView(source, work);
            var state = ReplicaPersistence.Decode<ReplicaHardState>(Required(view, ReplicaProtocol.StateStorageKey,
                configuration.MaxAppendBytes), configuration.MaxAppendEntries);
            ReplicaLogValidation.Validate(state, configuration);
            ReplicaBenchmarkMembership.Validate(Required(view, ReplicaBenchmarkMembership.StorageKey,
                configuration.MaxAppendBytes), true, configuration);
            var index = history.Receipt.AppliedPosition;
            if (index <= (state.Snapshot?.Index ?? 0) || index > state.CommittedIndex)
            {
                return null;
            }
            return ReplicaAdmittedEntryRead.Decode(Required(view, ReplicaProtocol.EntryStorageKey(index),
                configuration.MaxAppendBytes), index, state.Term, configuration.MaxAppendEntries, database);
        });
        if (entry is null)
        {
            return null;
        }
        var original = entry.Operation ?? throw DropTableLifecycleWitnessHistory.Invalid();
        token.ThrowIfCancellationRequested();
        var applied = NativeSerialization.Deserialize<long>(Required(canonical, KeySpace.AppliedBytes,
            IntegrationExecutionOptions.DatabaseLimits().Value.MaxBatchBytes));
        if (applied < entry.Index || history.Original is null
            || !database.NativeOperationsEqual(original, history.Original)
            || !ResourceRetirementValidation.Same(original, history.Original))
        {
            throw DropTableLifecycleWitnessHistory.Invalid();
        }
        var tokenProof = new CommitToken(catalog.DefaultShard.Incarnation, NativeCatalogRoute.PartitionId,
            entry.Index, catalog.DefaultShard.PlacementEpoch);
        var expected = new CommitReceipt(original.Id, tokenProof, [], DurabilityProfile.QuorumProcessDurable);
        if (!ResourceRetirementValidation.Same(expected, history.CanonicalReceipt))
        {
            throw DropTableLifecycleWitnessHistory.Invalid();
        }
        return new(original.Id, tokenProof);
    }

    private static byte[] Required(IKeyValueView view, byte[] key, int maximum)
    {
        byte[]? result = null;
        view.ReadValue(key, value => result = value.ToArray(), bytes =>
        {
            if (bytes > maximum)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, "The stopped DROP native record exceeds its original limit.");
            }
        });
        return result ?? throw DropTableLifecycleWitnessHistory.Invalid();
    }
}
