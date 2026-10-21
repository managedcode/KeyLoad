using KeyLoad.Core.Features.AnalyticsStore;
using ZoneTree.Analytics;

namespace KeyLoad.Core;

/// <summary>One real engine over a host-owned empty derived generation; no canonical storage gate is retained.</summary>
public sealed class AnalyticsStoreGeneration : IDisposable
{
    private const int StartedState = 1;
    private const int NotStartedState = 0;
    private const long TrackedEventStep = 1;
    private const string OccupiedGeneration = "The analytics projection generation is not empty.";
    private const string IncompleteProjection = "The analytics projection is incomplete.";
    private const string DeadlineExceeded = "The read execution deadline is exceeded.";
    private const string AlreadyQueried = "The analytics projection generation was already queried.";
    private const long EmptyEvents = 0;
    private readonly AnalyticsAggregateSnapshot snapshot;
    private readonly AnalyticsOptions options;
    private readonly AnalyticsStoreDimensions dimensions;
    private AnalyticsEngine? engine;
    private bool acquisitionAttempted;
    private bool disposed;
    private readonly AnalyticsStoreFailures failures = new();
    private int started;
    private long tracked;

    /// <summary>Captures validated options for a fresh host-owned generation; execution acquisition starts only inside Query.</summary>
    /// <param name="snapshot">The original complete bounded authorized canonical inputs.</param>
    /// <param name="ownedEmptyDirectory">A fresh host-owned generation directory, never a caller-selected path.</param>
    public AnalyticsStoreGeneration(AnalyticsAggregateSnapshot snapshot, string ownedEmptyDirectory)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Work.Check();
        if (snapshot.Rows.IsDefault || snapshot.Rows.Length > snapshot.Limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnalyticsStoreValidation.Budget);
        }
        if (Directory.Exists(ownedEmptyDirectory) && Directory.EnumerateFileSystemEntries(ownedEmptyDirectory).Any())
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, OccupiedGeneration);
        }

        this.snapshot = snapshot;
        dimensions = new(snapshot);
        var names = snapshot.Request.GroupBy.Select(name => AnalyticsStoreMapping.Dimension(snapshot.Schema, name)).ToArray();
        options = AnalyticsStoreOptions.Create(ownedEmptyDirectory, snapshot.Limits, names,
            snapshot.Request.Metrics.Where(value => value.Operation != AnalyticsAggregateKind.Count)
                .Select(value => value.Metric).Distinct(StringComparer.Ordinal).Count(), failures);
        IncludeCapturedTimestamps();
        options.Queries.MaxGroups = snapshot.Request.MaxGroups;
        options.Queries.Timeout = snapshot.Work.RemainingLifetime;
    }

    /// <summary>Gets whether the actual execution engine has completed successful joined disposal.</summary>
    public bool HasJoinedEngine
    {
        get; private set;
    }

    /// <summary>Builds and queries this one generation using the unchanged original request and cancellation.</summary>
    /// <param name="cancellationToken">Original operation/owner cancellation, not a new independent deadline.</param>
    /// <returns>Complete finite execution aggregate rows.</returns>
    public AnalyticsAggregateResult Query(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref started, StartedState) != NotStartedState)
        {
            throw Errors.Fail(ErrorCode.Conflict, AlreadyQueried);
        }

        using var deadline = new CancellationTokenSource(snapshot.Work.RemainingLifetime, snapshot.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, snapshot.Work.Cancellation, deadline.Token);
        try
        {
            return QueryOwned(linked.Token);
        }
        catch (AnalyticsQueryException error) when (AnalyticsStoreGroupLimit.Exceeded(snapshot))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnalyticsStoreValidation.Budget, error);
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested && !snapshot.Work.Cancellation.IsCancellationRequested)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded, error);
        }
    }

    private AnalyticsAggregateResult QueryOwned(CancellationToken token)
    {
        snapshot.Work.Check();
        token.ThrowIfCancellationRequested();
        acquisitionAttempted = true;
        var current = engine = new AnalyticsEngine(options);
        foreach (var row in snapshot.Rows)
        {
            snapshot.Work.Check();
            token.ThrowIfCancellationRequested();
            if (!current.Track(AnalyticsStoreMapping.Event(snapshot, dimensions, row)))
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, IncompleteProjection);
            }

            tracked = checked(tracked + TrackedEventStep);
            failures.RequireHealthy();
        }
        current.Flush(token);
        failures.RequireHealthy();
        snapshot.Work.Check();
        var result = current.Query(AnalyticsStoreMapping.Query(snapshot), token);
        failures.RequireHealthy();
        snapshot.Work.Check();
        var statistics = current.GetStatistics(false);
        if (statistics.EventsAccepted != tracked || statistics.EventsProcessed != tracked
            || statistics.EventsDropped != EmptyEvents || statistics.EventsRejected != EmptyEvents || statistics.Errors != EmptyEvents
            || !result.Plan.IsComplete || tracked > EmptyEvents && !result.Plan.UsedRawEvents
            || result.Plan.Steps.Any(step => step.Event != AnalyticsStoreOptions.Event || step.Source != PlanSource.RawEvents)
            || result.Plan.UsedRollups || result.Plan.UsedDimensionIndexes
            || result.Plan.UsedRollupIndexes || result.Rows.Count != result.TotalRows)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, IncompleteProjection);
        }

        return AnalyticsStoreResult.Create(snapshot, dimensions, result);
    }

    private void IncludeCapturedTimestamps()
    {
        // The engine compares timestamps with system UTC; canonical rows already own their exact UTC values.
        var now = TimeProvider.System.GetUtcNow();
        var latest = now;
        foreach (var row in snapshot.Rows)
        {
            snapshot.Work.Check();
            if (row.Timestamp > latest)
            {
                latest = row.Timestamp;
            }
        }
        var skew = latest - now;
        if (skew > options.Ingestion.MaxFutureSkew)
        {
            options.Ingestion.MaxFutureSkew = skew;
        }
    }

    /// <summary>Joins the actual execution close; failures retain an unjoined generation for its physical owner.</summary>
    public void Dispose()
    {
        disposed = true;
        if (!acquisitionAttempted)
        {
            HasJoinedEngine = true;
            return;
        }
        if (engine is null)
        {
            return;
        }
        engine.Dispose();
        engine = null;
        HasJoinedEngine = true;
    }
}
