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
    internal static Result<object> ResolveSubmissionOutcome(this DatabaseEngine engine, Command<DatabaseCommand> operation, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        var normalized = engine.OwnCommand(operation);
        return engine.Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var result = engine.ReadValidatedSubmissionOutcome(view, normalized);
            ConfiguredResourceOutcome.ObserveConfiguredResourceOutcome(view, normalized, result, work);
            work.Check();
            return result;
        });
    }
}
