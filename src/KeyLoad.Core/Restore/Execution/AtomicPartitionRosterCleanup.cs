using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BackupRestore.Execution;

internal sealed partial class AtomicPartitionRosterTransaction
{
    private ResourceRetirementCleanupScope? cleanupScope;
    internal long CleanupReplicationIndex => appliedIndex;

    internal void UseRetirementCleanup(IKeyValueView proofView, ResourceRetirementCleanupPageBodyV1 body,
        ReplicatedOperation operation, Guid currentIncarnation)
    {
        if (cleanupScope is not null || candidates.Count != NoCandidates)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }
        ResourceRetirementCleanupProvenance.Require(proofView, body.SealedState.Control.Lifetime, body.Partition,
            currentIncarnation, storePosition, limits.Value);
        cleanupScope = new(body, operation, appliedIndex > AtomicPartitionRosterProtocol.NoReplicatedAppliedIndex);
    }

    public void ValidateCandidate(IReadOnlyList<StorageMutation> mutations)
    {
        if (cleanupScope is null || inner is not IAtomicCommitCandidateValidation capability)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ResourceRetirementCleanupScope.CandidateUnsupported);
        }
        foreach (var mutation in mutations)
        {
            cleanupScope.RequireMutation(mutation.Key.ToArray(), mutation.Value.HasValue);
        }
        capability.ValidateCandidate(mutations);
    }
}
