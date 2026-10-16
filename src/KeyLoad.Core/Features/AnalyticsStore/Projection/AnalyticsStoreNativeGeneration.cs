using KeyLoad.Core.Features.AnalyticsStore;
using ZoneTree.Analytics;

namespace KeyLoad.Core;

/// <summary>One real engine over a host-owned empty derived generation; no canonical storage gate is retained.</summary>
public sealed class AnalyticsStoreNativeGeneration : IAsyncDisposable
{
    private const int StartedState = 1;
    private const int NotStartedState = 0;
    private const long TrackedEventStep = 1;
    private const string OccupiedGeneration = "The analytics projection generation is not empty.";
    private const string IncompleteProjection = "The analytics projection is incomplete.";
    private const string IncompleteAcquisition = "The native analytics engine acquisition did not settle.";
    private const string DeadlineExceeded = "The read execution deadline is exceeded.";
    private const string AlreadyQueried = "The analytics projection generation was already queried.";
    private const long EmptyEvents = 0;
    private readonly AnalyticsAggregateSnapshot snapshot;
    private readonly AnalyticsOptions options;
    private readonly AnalyticsStoreNativeDimensions dimensions;
    private AnalyticsEngine? engine;
    private bool acquisitionAttempted;
    private readonly AnalyticsStoreNativeFailures failures = new();
    private int started;
    private long tracked;

    /// <summary>Captures validated options for a fresh host-owned generation; native acquisition starts only inside QueryAsync.</summary>
    /// <param name="snapshot">The original complete bounded authorized canonical inputs.</param>
    /// <param name="ownedEmptyDirectory">A fresh host-owned generation directory, never a caller-selected path.</param>
    public AnalyticsStoreNativeGeneration(AnalyticsAggregateSnapshot snapshot, string ownedEmptyDirectory)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Work.Check();
        if (Directory.Exists(ownedEmptyDirectory) && Directory.EnumerateFileSystemEntries(ownedEmptyDirectory).Any())
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, OccupiedGeneration);
        }

        this.snapshot = snapshot;
        dimensions = new(snapshot);
        var names = snapshot.Request.GroupBy.Select(name => AnalyticsStoreNativeMapping.Dimension(snapshot.Schema, name)).ToArray();
        options = AnalyticsStoreNativeOptions.Create(ownedEmptyDirectory, snapshot.Limits, names,
            snapshot.Request.Metrics.Where(value => value.Operation != AnalyticsAggregateKind.Count)
                .Select(value => value.Metric).Distinct(StringComparer.Ordinal).Count(), failures);
        options.Queries.MaxGroups = snapshot.Request.MaxGroups;
        options.Queries.Timeout = snapshot.Work.RemainingLifetime;
    }

    /// <summary>Gets whether the actual native engine has completed successful joined disposal.</summary>
    public bool HasJoinedEngine
    {
        get; private set;
    }

    /// <summary>Builds and queries this one generation using the unchanged original request and cancellation.</summary>
    /// <param name="cancellationToken">Original operation/owner cancellation, not a new independent deadline.</param>
    /// <returns>Complete finite native aggregate rows.</returns>
    public async Task<AnalyticsAggregateResult> QueryAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref started, StartedState) != NotStartedState)
        {
            throw Errors.Fail(ErrorCode.Conflict, AlreadyQueried);
        }

        using var deadline = new CancellationTokenSource(snapshot.Work.RemainingLifetime, snapshot.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, snapshot.Work.Cancellation, deadline.Token);
        try
        {
            return await QueryOwnedAsync(linked.Token).ConfigureAwait(false);
        }
        catch (AnalyticsQueryException error) when (AnalyticsStoreNativeGroupLimit.Exceeded(snapshot))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnalyticsStoreValidation.Budget, error);
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested && !snapshot.Work.Cancellation.IsCancellationRequested)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded, error);
        }
    }

    private async Task<AnalyticsAggregateResult> QueryOwnedAsync(CancellationToken token)
    {
        snapshot.Work.Check();
        token.ThrowIfCancellationRequested();
        acquisitionAttempted = true;
        var current = engine = new AnalyticsEngine(options);
        foreach (var row in snapshot.Rows)
        {
            snapshot.Work.Check();
            if (!await current.TrackAsync(AnalyticsStoreNativeMapping.Event(snapshot, dimensions, row), token).ConfigureAwait(false))
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, IncompleteProjection);
            }

            tracked = checked(tracked + TrackedEventStep);
            failures.RequireHealthy();
        }
        await current.FlushAsync(token).ConfigureAwait(false);
        failures.RequireHealthy();
        snapshot.Work.Check();
        var result = await current.QueryAsync(AnalyticsStoreNativeMapping.Query(snapshot), token).ConfigureAwait(false);
        failures.RequireHealthy();
        snapshot.Work.Check();
        var statistics = current.GetStatistics(false);
        if (statistics.EventsAccepted != tracked || statistics.EventsProcessed != tracked
            || statistics.EventsDropped != EmptyEvents || statistics.EventsRejected != EmptyEvents || statistics.Errors != EmptyEvents
            || !result.Plan.IsComplete || tracked > EmptyEvents && !result.Plan.UsedRawEvents
            || result.Plan.Steps.Any(step => step.Event != AnalyticsStoreNativeOptions.Event || step.Source != PlanSource.RawEvents)
            || result.Plan.UsedRollups || result.Plan.UsedDimensionIndexes
            || result.Plan.UsedRollupIndexes || result.Rows.Count != result.TotalRows)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, IncompleteProjection);
        }

        return AnalyticsStoreNativeResult.Create(snapshot, dimensions, result);
    }

    /// <summary>Awaits the actual native close; failures retain an unjoined generation for its physical owner.</summary>
    /// <returns>Completion after the native engine joins and releases its locks.</returns>
    public async ValueTask DisposeAsync()
    {
        if (!acquisitionAttempted)
        {
            HasJoinedEngine = true;
            return;
        }
        var current = engine ?? throw Errors.Fail(ErrorCode.OwnershipLost, IncompleteAcquisition);
        await current.DisposeAsync().ConfigureAwait(false);
        HasJoinedEngine = true;
    }
}
