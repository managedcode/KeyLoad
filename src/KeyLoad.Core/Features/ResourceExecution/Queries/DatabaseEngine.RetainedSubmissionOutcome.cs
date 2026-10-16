using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Reads an actual retained result after the owner's applied quorum cut without changing new-command denials.</summary>
    internal OperationResult? ReadRetainedSubmissionOutcome(ReplicatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var normalized = NormalizeOperation(operation);
        return Store.Read<OperationResult?>(view =>
        {
            try
            {
                return ReadValidatedSubmissionOutcome(view, normalized, allowStoredDocumentDenial: true);
            }
            catch (KeyLoadException missing) when (missing.Code == ErrorCode.RecoveryRequired
                && string.Equals(missing.Message, MissingDurableOutcomeMessage, StringComparison.Ordinal))
            {
                return null;
            }
            catch (KeyLoadException refusal) when (refusal.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
                or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome))
            {
                if (HasRetainedSubmissionOutcome(view, normalized))
                {
                    throw;
                }
                // A new command keeps the existing canonical failed-outcome boundary.
                return null;
            }
        });
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
