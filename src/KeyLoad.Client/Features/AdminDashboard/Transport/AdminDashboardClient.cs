using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Reads actual administrator-authorized physical node observations.</summary>
    /// <param name="cancellationToken">Cancels the observation request.</param>
    /// <returns>The executing node's bounded storage and process observations.</returns>
    public Task<Result<AdminNodeSnapshot>> DashboardAsync(CancellationToken cancellationToken = default) =>
        Send<AdminNodeSnapshot>(AdminDashboardProtocol.SnapshotPath, null, false, null, cancellationToken);

    /// <summary>Reads a bounded administrator-authorized database resource metadata page.</summary>
    /// <param name="request">Explicit tenant/database scope and exclusive continuation.</param>
    /// <param name="cancellationToken">Cancels the bounded catalog read.</param>
    /// <returns>Nonsecret metadata and its read cut.</returns>
    public Task<Result<AdminResourcesPage>> ListResourcesAsync(AdminResourcesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<AdminResourcesPage>(AdminDashboardProtocol.ResourcesPath, request, false, null, cancellationToken);
    }

    /// <summary>Observes queue counters and metadata without receiving or altering messages.</summary>
    /// <param name="request">Full atomic queue identity and exclusive continuation.</param>
    /// <param name="cancellationToken">Cancels the bounded queue read.</param>
    /// <returns>Persisted queue counters and metadata from one read cut.</returns>
    public Task<Result<AdminQueuePage>> BrowseQueueAsync(AdminQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<AdminQueuePage>(AdminDashboardProtocol.QueuePath, request, false, null, cancellationToken);
    }
}
