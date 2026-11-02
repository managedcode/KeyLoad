using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ResourceExecutionQueriesResolveSubmissionOutcome operations against the caller's database context.</summary>
public static class ResourceExecutionQueriesResolveSubmissionOutcome
{
    /// <summary>Resolves the actual reauthorized outcome inside the original publication read budget.</summary>
    /// <param name = "operation">Unchanged original execution command identity and content.</param>
    /// <param name = "work">The same original bounded read execution and cancellation.</param>
    /// <returns>The complete canonical result after all existing current authority checks.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    internal static Result<object> /* KLD0041: This read returns the original native stored outcome selected by its stable command identity. */ ResolveSubmissionOutcome(this DatabaseEngine engine, Command<DatabaseCommand> operation, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(operation) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(work) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        work.Check();
        var normalized = engine.OwnCommand(operation);
        return engine.Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var /* KLD0041: The selected native persisted outcome retains the payload type of the original operation. */ result = engine.ReadValidatedSubmissionOutcome(view, normalized) /* KLD0041: This read validates identity and fresh authority before exposing the original native stored outcome. */;
            ConfiguredResourceOutcome.ObserveConfiguredResourceOutcome(view, normalized, result.Problem, work);
            work.Check();
            return result;
        }) /* KLD0041: The atomic read retains the complete original stored result at the same authorized snapshot. */;
    }
}
