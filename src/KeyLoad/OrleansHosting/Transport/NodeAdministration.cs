using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Observes the addressed silo and joins physical backups before its storage owner closes.</summary>
internal sealed class NodeAdministration(PartitionHost partition, IServiceProvider services)
    : INodeAdministration, IAsyncDisposable
{
    private const string BackupDirectory = "backups";
    private const string AdministrationStopping = "Node administration is stopping.";
    private readonly Lock lifecycle = new();
    private readonly HashSet<Task> backups = [];
    private Task? shutdown;

    /// <inheritdoc />
    public Task<BackupReceipt> BackupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (lifecycle)
        {
            if (shutdown is not null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, AdministrationStopping);
            }
            backups.Add(completion.Task);
        }
        return RunBackupAsync(completion, cancellationToken);
    }

    private async Task<BackupReceipt> RunBackupAsync(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateBackupAsync(cancellationToken);
        }
        finally
        {
            lock (lifecycle)
            {
                backups.Remove(completion.Task);
            }
            completion.TrySetResult();
        }
    }

    private async Task<BackupReceipt> CreateBackupAsync(CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString(OrleansNodeProtocol.GuidFormat);
        var position = await partition.Database.Store.CreateBackupAsync(
            Path.Combine(partition.DirectoryPath, BackupDirectory, id), cancellationToken);
        return new(id, position);
    }

    /// <inheritdoc />
    public Task<AdminNodeSnapshot> DashboardAsync(CancellationToken cancellationToken)
        => services.GetRequiredService<AdminNodeObserver>().ReadAsync(cancellationToken);

    /// <inheritdoc />
    public Task<NodeStatus> StatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = partition.Database.Store.Identity;
        var membership = services.GetRequiredService<IClusterMembershipService>().CurrentSnapshot;
        var silos = membership.Members.Values.Count(member => member.Status == SiloStatus.Active);
        var copies = services.GetRequiredService<IOptions<NodeOptions>>().Value.ReplicaCount;
        return Task.FromResult(new NodeStatus(identity.NodeId.ToString(), identity.Incarnation,
            partition.Database.LastApplied, silos, copies, partition.Database.Durability,
            services.GetRequiredService<OrleansNode>().DatabaseReady, Environment.ProcessId, identity.ReadGeneration));
    }

    /// <summary>Joins all accepted backups before the silo-local owner closes its store.</summary>
    public async ValueTask DisposeAsync()
    {
        Task stopping;
        lock (lifecycle)
        {
            shutdown ??= Task.WhenAll(backups);
            stopping = shutdown;
        }
        await stopping;
    }

}
