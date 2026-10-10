using KeyLoad.Storage;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

internal sealed class RemoteTransferPendingBudget : IDisposable
{
    private readonly ReadExecutionBudget budget;
    private readonly ReadExecutionBudgetReadGrant grant;
    private readonly ReadExecutionBudgetReadGrantLease lease;
    private readonly TimeProvider clock;
    private readonly TimeSpan deadline;
    private readonly long started;
    internal RemoteTransferPendingBudget(DatabaseEngine database, CancellationToken token)
        : this(database, coordination: false, token) { }

    internal static RemoteTransferPendingBudget Coordination(DatabaseEngine database, CancellationToken token)
        => new(database, coordination: true, token);

    private RemoteTransferPendingBudget(DatabaseEngine database, bool coordination, CancellationToken token)
    {
        clock = database.EvaluationClock;
        started = clock.GetTimestamp();
        deadline = database.DueDiscoveryDeadline;
        budget = new(database.OperationLimitsOptions, clock, token);
        grant = coordination
            ? budget.CreateReadGrant(budget.MaximumNativeReadBytes, budget.MaximumNativeScanRecords)
            : budget.CreateReadGrant(Math.Min(database.DueExecution.MaximumRangeBytes, budget.MaximumNativeReadBytes),
                Math.Min(database.DueExecution.MaximumRecordsPerPage, budget.MaximumNativeScanRecords));
        lease = budget.EnterReadGrant(grant);
    }
    internal int Remaining => grant.RemainingRecords;
    internal void Observe(long bytes) { Check(); budget.ChargeReadGrant(grant, bytes); }
    internal void Check()
    {
        budget.Check();
        if (clock.GetElapsedTime(started) > deadline)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, DueWorkProtocol.DeadlineExceeded); }
    }
    internal IKeyValueView View(IKeyValueView view) => budget.CreateView(view);
    internal void Result(object? value) => budget.CheckResult(value);
    public void Dispose() { lease.Dispose(); budget.CompleteReadGrant(grant); }
}
