using System.Runtime.ExceptionServices;
using KeyLoad.Core.Features.BackupRestore.Execution;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string CleanupCandidateFailures = "KeyLoad.RetirementCleanup.CandidateFailures";

    private (ResourceRetirementTableCleanupSelection Selection, ResourceRetirementCleanupMetadata Metadata)
        SelectCleanupCandidate(AtomicPartitionRosterTransaction roster, PrincipalRecord principal,
        ReplicatedOperation operation, ResourceRetirementCleanupPageBodyV1 body, ResourceRetirementPhaseReceiptV1 receipt,
        ResourceRetirementTableCleanupPlan plan, ResourceRetirementCleanupReadView work, byte[] outcomeKey)
    {
        var failures = new List<Exception>();
        var minimum = plan.Count == ResourceRetirementProtocol.Empty
            ? ResourceRetirementProtocol.Empty : ResourceRetirementProtocol.First;
        for (var count = plan.Count; count >= minimum; count--)
        {
            try
            {
                var selection = plan.Select(count);
                var metadata = EncodeCleanupMetadata(roster, principal, operation, body, receipt, selection, work, outcomeKey);
                ValidateCleanupCandidate(roster, selection, metadata);
                return (selection, metadata);
            }
            catch (KeyLoadException error) when (error.Code is ErrorCode.ResourceExhausted or ErrorCode.BudgetExceeded)
            {
                failures.Add(error);
            }
        }
        var last = failures[^ResourceRetirementProtocol.First];
        last.Data[CleanupCandidateFailures] = failures.ToArray();
        ExceptionDispatchInfo.Throw(last);
        throw new InvalidOperationException(ResourceRetirementTableCleanupFamilies.Accounting);
    }

    private void ValidateCleanupCandidate(IAtomicCommitCandidateValidation capability,
        ResourceRetirementTableCleanupSelection selection, ResourceRetirementCleanupMetadata metadata)
    {
        var mutations = selection.Deletes.Concat(metadata.Mutations).ToArray();
        long bytes = ResourceRetirementProtocol.Empty;
        foreach (var mutation in mutations)
        {
            bytes = checked(bytes + mutation.Key.Length + (mutation.Value?.Length ?? ResourceRetirementProtocol.Empty));
        }
        if (mutations.Length > Limits.MaxBatchMutations || bytes > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementTableCleanupFamilies.Capacity);
        }
        capability.ValidateCandidate(mutations);
    }
}
