using KeyLoad.Storage;

namespace KeyLoad.Core;

internal sealed class RemoteTransferPendingBudget
{
    private readonly ReadExecutionBudget budget;
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
        : this(database, coordination, new ReadExecutionBudget(database.EvaluationClock, token))
    {
    }

    private RemoteTransferPendingBudget(DatabaseEngine database, bool coordination, ReadExecutionBudget original)
    {
        clock = database.EvaluationClock;
        started = clock.GetTimestamp();
        deadline = database.DueDiscoveryDeadline;
        budget = original;
        Remaining = coordination ? int.MaxValue : database.DueExecution.MaximumRecordsPerPage;
    }
    internal int Remaining { get; }
    internal void Observe(long _)
    {
        Check();
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
}
