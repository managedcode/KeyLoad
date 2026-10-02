using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Physical-node administration borrowed by authorized read actors; never owns database files.</summary>
/// <param name="partition">Actual node storage and replica owner.</param>
/// <param name="commands">Configured command admission governor.</param>
/// <param name="http">Configured HTTP admission governor.</param>
/// <param name="services">Outer application provider for lazy routing readiness lookup.</param>
internal sealed class NodeAdministration(PartitionHost partition, CommandAdmissionGovernor commands,
    HttpAdmissionGovernor http, IServiceProvider services) : INodeAdministration, IAsyncDisposable
{
    private const string BackupDirectory = "backups";
    private const string BackupBusy = "The node already has a backup in progress.";
    private const string AdministrationStopping = "Node administration is stopping.";
    private readonly object lifecycle = new();
    private Task<BackupReceipt>? backup;
    private Task? shutdown;

    /// <inheritdoc />
    public Task<BackupReceipt> BackupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            if (shutdown is not null)
            { throw Errors.Fail(ErrorCode.OwnershipLost, AdministrationStopping); }
            if (backup is { IsCompleted: false })
            { throw Errors.Fail(ErrorCode.ResourceExhausted, BackupBusy); }
            backup = Task.Run(() => CreateBackup(cancellationToken), cancellationToken);
            return backup.WaitAsync(cancellationToken);
        }
    }

    private BackupReceipt CreateBackup(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString(OrleansNodeProtocol.GuidFormat);
        var position = partition.Database.Store.CreateBackup(Path.Combine(partition.DirectoryPath, BackupDirectory, id));
        return new(id, position);
    }

    /// <inheritdoc />
    public NodeAdmissionStatus Admission() => new(commands.Limits, commands.Snapshot()) { Http = http.Status() };

    /// <inheritdoc />
    public async Task<NodeStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var state = await partition.Consensus.StateAsync(cancellationToken).ConfigureAwait(false);
        var identity = partition.Database.Store.Identity;
        return new(identity.NodeId.ToString(), identity.Incarnation, state.MaterializedPosition, state.LeaderId,
            partition.Configuration.VoterIds.Length, partition.Database.Durability,
            services.GetRequiredService<OrleansNode>().Grains is not null, Environment.ProcessId, identity.ReadGeneration);
    }

    /// <summary>Observes and drains any accepted backup before the physical owner closes its store.</summary>
    public async ValueTask DisposeAsync()
    {
        Task stopping;
        lock (lifecycle)
        { shutdown ??= backup ?? Task.CompletedTask; stopping = shutdown; }
        // The request observes its backup failure; cleanup must still release every physical owner.
        await stopping.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
