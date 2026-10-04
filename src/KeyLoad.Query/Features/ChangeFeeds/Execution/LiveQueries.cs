namespace KeyLoad.Query;

public sealed partial class QueryEngine
{
    /// <summary>Captures a complete authorized scalar snapshot and change-feed cursor in one read cut.</summary>
    /// <param name="principalId">Persisted database principal identifier.</param>
    /// <param name="request">Live-query snapshot request.</param>
    /// <param name="timeProvider">Optional operation clock for deadlines.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The complete snapshot and its continuation cursor.</returns>
    public LiveQuerySnapshot StartLiveQuery(string principalId, StartLiveQueryRequest request,
        TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
        => liveQueries.Start(principalId, request, timeProvider, cancellationToken);

    /// <summary>Reads authorized live-query changes using the supplied continuation.</summary>
    /// <param name="principalId">Persisted database principal identifier.</param>
    /// <param name="request">Live-query continuation request.</param>
    /// <param name="timeProvider">Optional operation clock for deadlines.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A bounded change page with updated continuation.</returns>
    public LiveQueryPage ReadLiveQuery(string principalId, ReadLiveQueryRequest request,
        TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
        => liveQueries.Read(principalId, request, timeProvider, cancellationToken);
}
