using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.CommandOutcomes.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes RetainedSubmissionOutcome operations against the caller's database context.</summary>
public static class RetainedSubmissionOutcome
{
    /// <summary>Reads an actual retained result after the owner's committed database cut without changing new-command denials.</summary>

    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "operation">The operation.</param>
    internal static Result<object>? ReadRetainedSubmissionOutcome(this DatabaseEngine engine, Command<DatabaseCommand> operation) => engine.ReadRetainedSubmissionOutcomeCore(operation, null);
    /// <summary>Charges retained lookup to the same original admission work without changing canonical denial semantics.</summary>
    /// <param name = "operation">Unchanged original execution identity and payload.</param>
    /// <param name = "work">Required original admission budget, cancellation and lifetime.</param>
    /// <returns>The actual retained result, or null only at the existing absent-outcome boundary.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    internal static Result<object>? ReadRetainedSubmissionOutcome(this DatabaseEngine engine, Command<DatabaseCommand> operation, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return engine.ReadRetainedSubmissionOutcomeCore(operation, work);
    }

    internal static Result<object>? ReadRetainedSubmissionOutcomeCore(this DatabaseEngine engine, Command<DatabaseCommand> operation, ReadExecutionBudget? work)
    {
        ArgumentNullException.ThrowIfNull(operation);
        work?.Check();
        var normalized = engine.OwnCommand(operation);
        work?.Check();
        var result = engine.Store.Read<Result<object>?>(raw =>
        {
            var view = work is null ? raw : work.CreateView(raw);
            try
            {
                return engine.ReadValidatedSubmissionOutcome(view, normalized);
            }
            catch (KeyLoadException missing) when (missing.Code == ErrorCode.RecoveryRequired && string.Equals(missing.Message, CommandOutcomeOperations.MissingDurableOutcomeMessage, StringComparison.Ordinal))
            {
                work?.Check();
                return null;
            }
            catch (KeyLoadException refusal) when (refusal.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome) && (work is null || refusal.Code != ErrorCode.BudgetExceeded))
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

    internal static bool HasRetainedSubmissionOutcome(IKeyValueView view, Command<DatabaseCommand> operation)
    {
        byte[] key;
        try
        {
            var scope = CommandOutcomePartitionIdentity.Resolve(operation);
            key = CommandOutcomeKeyResolver.ForNew(operation.UserId!, operation.CommandId, scope).Key;
        }
        catch (KeyLoadException)
        {
            // Do not expose payload/scope diagnostics ahead of the original authorization refusal.
            return false;
        }

        return view.ReadValue(key, static _ =>
        {
        });
    }
}
