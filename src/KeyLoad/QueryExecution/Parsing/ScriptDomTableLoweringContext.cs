using KeyLoad.Core;

namespace KeyLoad.Server;

internal sealed class ScriptDomTableLoweringContext
{
    private readonly Action checkBudget;
    private readonly CancellationToken token;
    internal ScriptDomTableLoweringContext(Action checkBudget,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(checkBudget);
        this.checkBudget = checkBudget;
        this.token = token;
    }
    internal void Check()
    {
        token.ThrowIfCancellationRequested();
        checkBudget();
        ExecutionStackGuard.Ensure();
    }
}
