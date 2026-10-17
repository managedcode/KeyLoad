using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Reads an actual retained result after the owner's applied quorum cut without changing new-command denials.</summary>
    internal OperationResult? ReadRetainedSubmissionOutcome(ReplicatedOperation operation)
        => ReadRetainedSubmissionOutcomeCore(operation, null);

    /// <summary>Charges retained lookup to the same original admission work without changing canonical denial semantics.</summary>
    /// <param name="operation">Unchanged original native identity and payload.</param>
    /// <param name="work">Required original admission budget, cancellation and lifetime.</param>
    /// <returns>The actual retained result, or null only at the existing absent-outcome boundary.</returns>
    internal OperationResult? ReadRetainedSubmissionOutcome(ReplicatedOperation operation, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return ReadRetainedSubmissionOutcomeCore(operation, work);
    }

    private OperationResult? ReadRetainedSubmissionOutcomeCore(ReplicatedOperation operation, ReadExecutionBudget? work)
    {
        ArgumentNullException.ThrowIfNull(operation);
        work?.Check();
        var normalized = work is null ? NormalizeOperation(operation) : VerifyOperationAuthority(operation);
        work?.Check();
        var result = Store.Read<OperationResult?>(raw =>
        {
            var view = work is null ? raw : work.CreateView(raw);
            try
            {
                return ReadValidatedSubmissionOutcome(view, normalized, allowStoredDocumentDenial: true);
            }
            catch (KeyLoadException missing) when (missing.Code == ErrorCode.RecoveryRequired
                && string.Equals(missing.Message, MissingDurableOutcomeMessage, StringComparison.Ordinal))
            {
                work?.Check();
                return null;
            }
            catch (KeyLoadException refusal) when (refusal.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
                or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome)
                && (work is null || refusal.Code != ErrorCode.BudgetExceeded))
            {
                work?.Check();
                if (HasRetainedSubmissionOutcome(view, normalized))
                {
                    throw;
                }
                // A new command keeps the existing canonical failed-outcome boundary.
                return null;
            }
        });
        work?.Check();
        return result;
    }

    private static bool HasRetainedSubmissionOutcome(IKeyValueView view, ReplicatedOperation operation)
    {
        byte[] key;
        try
        {
            var scope = CommandOutcomePartitionIdentity.Resolve(operation);
            key = CommandOutcomeKeyResolver.ForNew(operation.PrincipalId, operation.Id, scope).Key;
        }
        catch (KeyLoadException)
        {
            // Do not expose payload/scope diagnostics ahead of the original authorization refusal.
            return false;
        }
        return view.ReadValue(key, static _ => { });
    }
}
