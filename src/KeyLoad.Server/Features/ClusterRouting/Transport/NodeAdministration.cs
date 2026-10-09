using KeyLoad.Core;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Physical-node administration borrowed by authorized read actors; never owns database files.</summary>
/// <param name="partition">Actual node storage and replica owner.</param>
/// <param name="commands">Configured command admission governor.</param>
/// <param name="http">Configured HTTP admission governor.</param>
/// <param name="services">Outer application provider for lazy routing readiness lookup.</param>
/// <param name="captureOptions">Original centrally validated archive producer admission owner.</param>
internal sealed class NodeAdministration(PartitionHost partition, CommandAdmissionGovernor commands,
    HttpAdmissionGovernor http, IServiceProvider services, IOptions<ClusterBackupExecutionOptions> captureOptions) : INodeAdministration, IAsyncDisposable
{
    private const string BackupDirectory = "backups";
    private const string BackupBusy = "The node already has a backup in progress.";
    private const string AdministrationStopping = "Node administration is stopping.";
    private readonly Lock lifecycle = new();
    private Task? backup;
    private readonly HashSet<Task> captures = [];
    private Task? shutdown;

    /// <inheritdoc />
    public Task<BackupReceipt> BackupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            if (shutdown is not null)
            { throw Errors.Fail(ErrorCode.OwnershipLost, AdministrationStopping); }
            if (backup is { IsCompleted: false } || captures.Any(operation => !operation.IsCompleted))
            { throw Errors.Fail(ErrorCode.ResourceExhausted, BackupBusy); }
            var operation = Task.Run(() => CreateBackup(cancellationToken), cancellationToken);
            backup = operation;
            return operation.WaitAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task<ClusterBackupOwnerReceipt> CaptureClusterBackupOwnerAsync(string principalId,
        ReadOnlyMemory<byte> capability, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            if (shutdown is not null)
            { throw Errors.Fail(ErrorCode.OwnershipLost, AdministrationStopping); }
            if (backup is { IsCompleted: false })
            { throw Errors.Fail(ErrorCode.ResourceExhausted, BackupBusy); }
            RetireSettledCaptures();
            if (captures.Count >= captureOptions.Value.MaximumAdmissions)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, BackupBusy); }
            var operation = Task.Run(() => ClusterBackupOwnerArchive.Capture(partition, principalId,
                capability, work), cancellationToken);
            captures.Add(operation);
            return operation;
        }
    }

    private void RetireSettledCaptures()
    {
        // Reading Task.Exception observes the settled original failure; the returned task remains faulted.
        captures.RemoveWhere(operation => operation.IsCompleted
            && (!operation.IsFaulted || operation.Exception is not null));
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
    public Task<AdminNodeSnapshot> DashboardAsync(CancellationToken cancellationToken)
        => services.GetRequiredService<AdminNodeObserver>().ReadAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<NodeStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var state = await partition.Consensus.StateAsync(cancellationToken).ConfigureAwait(false);
        var identity = partition.Database.Store.Identity;
        return new(identity.NodeId.ToString(), identity.Incarnation, state.MaterializedPosition, state.LeaderId,
            partition.Configuration.VoterIds.Length, partition.Database.Durability,
            services.GetRequiredService<OrleansNode>().HasCompatibleCohort, Environment.ProcessId, identity.ReadGeneration)
        { ConsensusTerm = state.Term };
    }

    /// <summary>Observes and drains any accepted backup before the physical owner closes its store.</summary>
    public async ValueTask DisposeAsync()
    {
        Task stopping;
        lock (lifecycle)
        { shutdown ??= DrainAcceptedAsync(backup, captures.ToArray()); stopping = shutdown; }
        await stopping.ConfigureAwait(false);
    }
    private static async Task DrainAcceptedAsync(Task? ordinaryBackup, Task[] acceptedCaptures)
    {
        // Preserve the legacy ordinary-backup cleanup contract; capture failures stay native originals.
        if (ordinaryBackup is not null)
        { await ordinaryBackup.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
        var joined = Task.WhenAll(acceptedCaptures);
        try
        { await joined.ConfigureAwait(false); }
        catch (Exception failure)
        { throw joined.Exception ?? new AggregateException(failure); }
    }
}
