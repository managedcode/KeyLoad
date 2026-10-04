using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Observes persisted queue metadata without performing delivery or expiry transitions.</summary>
/// <param name="database">Borrowed canonical engine.</param>
public sealed class AdminQueueReader(DatabaseEngine database)
{
    private const string CounterSpace = "queue-counters";
    private const string MetadataSpace = "message-meta";

    /// <summary>Reads exact persisted counters and one bounded metadata page.</summary>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="request">Atomic queue scope and exclusive message continuation.</param>
    /// <param name="cancellationToken">Cancellation of bounded read work.</param>
    /// <returns>Nonsecret metadata from one gated cut without writing any record.</returns>
    public AdminQueuePage Read(string principalId, AdminQueueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Lane);
        DatabaseEngine.ValidatePartition(request.Lane.Partition);
        JsonData.Identifier(request.Lane.Queue);
        AdminReadAuthority.ValidatePage(request.Limit, request.AfterId);
        var budget = new ReadExecutionBudget(database.Limits, database.EvaluationClock, cancellationToken);
        return database.Store.Read(view => ReadPage(budget.CreateView(view), budget, principalId, request));
    }

    private AdminQueuePage ReadPage(IKeyValueView view, ReadExecutionBudget budget, string principalId, AdminQueueRequest request)
    {
        AdminReadAuthority.Require(database, view, principalId);
        var lane = request.Lane;
        _ = database.Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        var counters = view.GetRecord<QueueCounters>(KeySpace.Partition(CounterSpace, lane.Partition, lane.Queue)) ?? new(0, 0, 0, 0, 0);
        var items = new List<AdminQueueItem>();
        var after = request.AfterId is null ? null : KeySpace.Partition(MetadataSpace, lane.Partition, lane.Queue, request.AfterId);
        var scan = view.VisitRange(KeySpace.Partition(MetadataSpace, lane.Partition, lane.Queue), request.Limit,
            (_, value) =>
            {
                var metadata = NativeSerialization.Deserialize<MessageMetadata>(value);
                items.Add(new(metadata.Id, metadata.State, metadata.Attempts, metadata.StateVersion,
                    metadata.NotBefore, metadata.ExpiresAt, metadata.LeaseUntil));
                return true;
            }, after, cancellationToken: budget.Cancellation);
        var page = new AdminQueuePage(counters, [.. items], scan.HasMore ? items[^1].Id : null, AdminReadAuthority.Position(view));
        budget.CheckResult(page);
        return page;
    }
}
