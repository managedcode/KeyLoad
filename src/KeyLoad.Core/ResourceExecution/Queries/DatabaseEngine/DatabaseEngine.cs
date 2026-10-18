using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Resolves the actual reauthorized outcome inside the original publication read budget.</summary>
    /// <param name="operation">Unchanged original native command identity and content.</param>
    /// <param name="work">The same original bounded read execution and cancellation.</param>
    /// <returns>The complete canonical result after all existing current authority checks.</returns>
    internal Result<object> ResolveSubmissionOutcome(Command<DatabaseCommand> operation, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        var normalized = OwnCommand(operation);
        return Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var result = ReadValidatedSubmissionOutcome(view, normalized, allowStoredDocumentDenial: true);
            ObserveConfiguredResourceOutcome(view, normalized, result, work);
            work.CheckResult(result.Value ?? result);
            work.Check();
            return result;
        });
    }
}
