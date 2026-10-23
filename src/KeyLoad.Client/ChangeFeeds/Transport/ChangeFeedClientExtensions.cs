using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Change-feed and projection operations using the authenticated SDK transport.</summary>
public static class ChangeFeedClientExtensions
{
    /// <summary>Reads one bounded change-feed page.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">Feed identity and page continuation controls.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed change-feed page.</returns>
    public static Task<Result<ChangeFeedPage>> ReadChangesAsync(this KeyLoadClient client, ReadChangeFeedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<ChangeFeedPage>(ApiRoutes.ChangesRead, request, false, null, cancellationToken);
    }
    /// <summary>Starts a bounded live query snapshot.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">Live query selection and lifetime controls.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The initial live query snapshot.</returns>
    public static Task<Result<LiveQuerySnapshot>> StartLiveQueryAsync(this KeyLoadClient client, StartLiveQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<LiveQuerySnapshot>(ApiRoutes.LiveQueryStart, request, false, null, cancellationToken);
    }
    /// <summary>Reads a bounded page from an existing live query.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">Live query identity and page cursor.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed live query page.</returns>
    public static Task<Result<LiveQueryPage>> ReadLiveQueryAsync(this KeyLoadClient client, ReadLiveQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<LiveQueryPage>(ApiRoutes.LiveQueryRead, request, false, null, cancellationToken);
    }
    /// <summary>Reads the status of one projection outbox.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="partition">Partition whose outbox is inspected.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The persisted outbox status.</returns>
    public static Task<Result<OutboxStatus>> OutboxStatusAsync(this KeyLoadClient client, PartitionRef partition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<OutboxStatus>(ApiRoutes.OutboxStatus, new GetOutboxStatusRequest(partition), false, null, cancellationToken);
    }
    /// <summary>Purges an eligible prefix of one projection outbox.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">Purge range, partition and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting outbox head.</returns>
    public static Task<Result<OutboxHead>> PurgeOutboxAsync(this KeyLoadClient client, PurgeOutboxRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<OutboxHead>(ApiRoutes.OutboxPurge, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Creates or updates one projection consumer.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">Projection consumer policy and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting consumer information.</returns>
    public static Task<Result<ProjectionConsumerInfo>> ConfigureProjectionAsync(this KeyLoadClient client, ConfigureProjectionConsumerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<ProjectionConsumerInfo>(ApiRoutes.ProjectionsConfigure, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Reads one bounded projection outbox batch.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">Consumer identity, cursor and batch limits.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed projection batch.</returns>
    public static Task<Result<ProjectionBatch>> ReadProjectionAsync(this KeyLoadClient client, ReadProjectionBatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<ProjectionBatch>(ApiRoutes.ProjectionsRead, request, false, null, cancellationToken);
    }
    /// <summary>Commits one projection batch result using its stable command identifier.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">Consumer progress and projection effects.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed projection result.</returns>
    public static Task<Result<ProjectionBatchResult>> CommitProjectionAsync(this KeyLoadClient client, CommitProjectionBatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<ProjectionBatchResult>(ApiRoutes.ProjectionsCommit, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Releases one projection consumer lease.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">Consumer identity and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting consumer information.</returns>
    public static Task<Result<ProjectionConsumerInfo>> ReleaseProjectionAsync(this KeyLoadClient client, ReleaseProjectionConsumerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<ProjectionConsumerInfo>(ApiRoutes.ProjectionsRelease, request, true, request.CommandId, cancellationToken);
    }
}
