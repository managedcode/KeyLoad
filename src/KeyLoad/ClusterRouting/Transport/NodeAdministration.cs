using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Observes the addressed silo and joins physical backups before its storage owner closes.</summary>
internal sealed class NodeAdministration(PartitionHost partition, CommandAdmissionGovernor commands,
    HttpAdmissionGovernor http, IServiceProvider services) : INodeAdministration, IAsyncDisposable
{
    private const string BackupDirectory = "backups";
    private const string BackupBusy = "The node already has a backup in progress.";
    private const string AdministrationStopping = "Node administration is stopping.";
    private readonly Lock lifecycle = new();
    private Task? backup;
    private Task? shutdown;

    /// <inheritdoc />
    public Task<BackupReceipt> BackupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            if (shutdown is not null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, AdministrationStopping);
            }
            if (backup is { IsCompleted: false })
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, BackupBusy);
            }
            var operation = Task.FromResult(CreateBackup(cancellationToken));
            backup = operation;
            return operation.WaitAsync(cancellationToken);
        }
    }

    private BackupReceipt CreateBackup(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString(OrleansNodeProtocol.GuidFormat);
        var position = partition.Database.Store.CreateBackup(Path.Combine(partition.DirectoryPath, BackupDirectory, id),
            cancellationToken);
        return new(id, position);
    }

    /// <inheritdoc />
    public NodeAdmissionStatus Admission() => new(commands.Limits, commands.Snapshot()) { Http = http.Status() };

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

    /// <summary>Joins the accepted backup before the silo-local owner closes its store.</summary>
    public async ValueTask DisposeAsync()
    {
        Task stopping;
        lock (lifecycle)
        {
            shutdown ??= DrainAcceptedAsync(backup);
            stopping = shutdown;
        }
        await stopping.ConfigureAwait(false);
    }

    private static async Task DrainAcceptedAsync(Task? accepted)
    {
        if (accepted is not null)
        {
            await accepted.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
