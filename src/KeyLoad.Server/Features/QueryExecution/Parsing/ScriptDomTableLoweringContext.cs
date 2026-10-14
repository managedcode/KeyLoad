using Microsoft.Extensions.Options;
namespace KeyLoad.Server;

internal sealed class ScriptDomTableLoweringContext
{
    private readonly Action checkBudget;
    private readonly CancellationToken token;
    private readonly int maximumNodes;
    private int nodes;
    internal ScriptDomTableLoweringContext(IOptions<DatabaseLimits> configured, Action checkBudget,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(checkBudget);
        var limits = configured.Value;
        limits.Validate();
        maximumNodes = limits.MaxQueryTokens;
        this.checkBudget = checkBudget;
        this.token = token;
    }
    internal void Check()
    {
        token.ThrowIfCancellationRequested();
        checkBudget();
        if (++nodes > maximumNodes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, global::KeyLoad.Features.QueryExecution.ScriptDomErrors.NodesDetail); }
    }
}
