using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes QueueRetryAuthorization operations against the caller's database context.</summary>
public static class QueueRetryAuthorization
{
    internal static bool TryAuthorizeQueueRetry(this DatabaseEngine engine, IKeyValueView view, Command<DatabaseCommand> operation, out PrincipalRecord? principal, bool preserveBudgetFailure)
    {
        principal = null;
        try
        {
            principal = engine.Principal(view, operation.UserId!, engine.Clock.GetUtcNow());
            engine.AuthorizeOperation(view, principal, operation);
            return true;
        }
        catch (KeyLoadException failure) when (failure.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome) && (!preserveBudgetFailure || failure.Code != ErrorCode.BudgetExceeded))
        {
            principal = null;
            return false;
        }
    }
}
