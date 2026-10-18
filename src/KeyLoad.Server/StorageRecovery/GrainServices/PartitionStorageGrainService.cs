using KeyLoad.Clustering;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.CQRS;
using OpenTelemetry;

namespace KeyLoad.Server;

/// <summary>Opens this silo's one physical storage owner in the awaited native initialization phase.</summary>
/// <param name="grainId">The native service identity supplied by Orleans registration.</param>
/// <param name="silo">The actual owning silo, independent of logical partition activation placement.</param>
/// <param name="loggerFactory">The borrowed native logger factory.</param>
/// <param name="owner">The same silo-local owner retained through opening and eventual joined physical close.</param>
/// <param name="grains"></param>
/// <param name="services"></param>
internal sealed class PartitionStorageGrainService(GrainId grainId, Silo silo, ILoggerFactory loggerFactory,
    PartitionHostApplicationOwner owner, IGrainFactory grains, IServiceProvider services) : GrainService(grainId, silo, loggerFactory), ISiloDatabaseService
{
    private readonly PartitionHostApplicationOwner owner = owner;

    public Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = owner.Borrow().Database;
        return Task.FromResult(database.Store.Read(_ => new SiloStorageCut(database.Store.Identity.Incarnation, database.LastApplied)));
    }

    public Task<Command<DatabaseCommand>> PrepareAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
        => Task.FromResult(owner.Borrow().Database.PrepareStorageOperation(operation, cancellationToken));

    public Task<StoredCommand?> ReadStoredCommandAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
        => Task.FromResult(owner.Borrow().Database.ReadStoredCommand(operation, cancellationToken));

    public Task<StorageOperationResult> ApplyAsync(Command<DatabaseCommand> operation, long sequence, CancellationToken cancellationToken)
        => Task.FromResult(owner.Borrow().Database.ApplyStorageOperation(operation, sequence, cancellationToken));

    public IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken)
        => grains.GetGrain<IRequestWorkerGrain>(RequestWorkerIdentity.ForRequests())
            .ExecuteStreamAsync(signedRequest, cancellationToken);

    public Task<GrainOperationReply> ExecuteCommandAsync(string signedRequest, string actorKey,
        CancellationToken cancellationToken)
        => grains.GetGrain<IRequestWorkerGrain>(RequestWorkerIdentity.ForCommands())
            .ExecuteCommandAsync(signedRequest, actorKey, cancellationToken);

    public Task<NodeStatus> ReadStatusAsync(CancellationToken cancellationToken)
        => services.GetRequiredService<INodeAdministration>().StatusAsync(cancellationToken);

    public Task<NodeAdmissionStatus> ReadAdmissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(services.GetRequiredService<INodeAdministration>().Admission());
    }

    public Task<AdminNodeSnapshot> ReadDashboardAsync(CancellationToken cancellationToken)
        => services.GetRequiredService<INodeAdministration>().DashboardAsync(cancellationToken);

    public Task<BackupReceipt> CreateBackupAsync(CancellationToken cancellationToken)
        => services.GetRequiredService<INodeAdministration>().BackupAsync(cancellationToken);

    /// <inheritdoc />
    public override async Task Init(IServiceProvider serviceProvider)
    {
        serviceProvider.GetRequiredService<ITelemetryHostInitializer>().Initialize();
        await owner.OpenAsync().ConfigureAwait(true);
        await serviceProvider.GetRequiredService<OrleansNode>().InitializeLocalRuntimeAsync(CancellationToken.None).ConfigureAwait(true);
    }
}
