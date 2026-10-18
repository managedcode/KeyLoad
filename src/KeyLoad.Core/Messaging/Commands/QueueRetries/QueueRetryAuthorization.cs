using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private bool TryAuthorizeQueueRetry(IKeyValueView view, Command<DatabaseCommand> operation,
        out PrincipalRecord? principal, bool preserveBudgetFailure)
    {
        principal = null;
        try
        {
            principal = Principal(view, operation.UserId!, Clock.GetUtcNow());
            AuthorizeOperation(view, principal, operation);
            return true;
        }
        catch (KeyLoadException failure) when (failure.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
            or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome)
            && (!preserveBudgetFailure || failure.Code != ErrorCode.BudgetExceeded))
        {
            principal = null;
            return false;
        }
    }
}
