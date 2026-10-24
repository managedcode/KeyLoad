using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Storage;

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

    internal static RemoteTransferPendingBudget Coordination(DatabaseEngine database, ReadExecutionBudget original)
        => new(database, coordination: true, original);

    private RemoteTransferPendingBudget(DatabaseEngine database, bool coordination, CancellationToken token)
        : this(database, coordination, new ReadExecutionBudget(database.OperationLimitsOptions,
            database.EvaluationClock, token))
    {
    }

    private RemoteTransferPendingBudget(DatabaseEngine database, bool coordination, ReadExecutionBudget original)
    {
        clock = database.EvaluationClock;
        started = clock.GetTimestamp();
        deadline = database.DueDiscoveryDeadline;
        budget = original;
        grant = coordination
            ? budget.CreateReadGrant(budget.RuntimeMaximumReadBytes, budget.MaximumScanRecords)
            : budget.CreateReadGrant(Math.Min(database.DueExecution.MaximumRangeBytes, budget.RuntimeMaximumReadBytes),
                Math.Min(database.DueExecution.MaximumRecordsPerPage, budget.MaximumScanRecords));
        lease = budget.EnterReadGrant(grant);
    }
    internal int Remaining => grant.RemainingRecords;
    internal void Observe(long bytes)
    {
        Check();
        budget.ChargeReadGrant(grant, bytes);
    }
    internal void Check()
    {
        budget.Check();
        if (clock.GetElapsedTime(started) > deadline)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DueWorkProtocol.DeadlineExceeded);
        }
    }
    internal IKeyValueView View(IKeyValueView view) => budget.CreateView(view);
    internal void Result(object? value) => budget.CheckResult(value);
    public void Dispose()
    {
        lease.Dispose();
        budget.CompleteReadGrant(grant);
    }
}
