using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication.Commands;
using OpenTelemetry;

namespace KeyLoad.Server;

/// <summary>Opens this silo's one physical storage owner in the awaited execution initialization phase.</summary>
/// <param name="grainId">The execution service identity supplied by Orleans registration.</param>
/// <param name="silo">The actual owning silo, independent of logical partition activation placement.</param>
/// <param name="loggerFactory">The borrowed execution logger factory.</param>
/// <param name="owner">The same silo-local owner retained through opening and eventual joined physical close.</param>
/// <param name="services"></param>
internal sealed partial class PartitionStorageGrainService(GrainId grainId, Silo silo, ILoggerFactory loggerFactory,
    PartitionHostApplicationOwner owner, IServiceProvider services) : GrainService(grainId, silo, loggerFactory), ISiloDatabaseService
{
    private readonly PartitionHostApplicationOwner owner = owner;
    private bool UsesSharedVolume => services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ReplicaCopyOptions>>()
        .Value.StorageMode == StorageAccessMode.SharedVolume;
    private IDatabaseCommitGrain Shared => services.GetRequiredService<IGrainFactory>()
        .GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(services.GetRequiredService<GrainRequestCodec>().Incarnation));
    private IBackgroundStorageOperations BackgroundTarget => UsesSharedVolume ? Shared : owner.Session.Background;
    private PartitionStorageSession Isolated => !UsesSharedVolume ? owner.Session
        : throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);

    public Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken)
    {
        return UsesSharedVolume ? Shared.ReadCutAsync(cancellationToken) : Task.FromResult(Isolated.ReadCut(cancellationToken));
    }

    public Task<Command<DatabaseCommand>> PrepareAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
        => Task.FromResult(Isolated.Host.Database.PrepareStorageOperation(operation, cancellationToken));

    public Task<StoredCommand?> ReadStoredCommandAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
        => Task.FromResult(Isolated.Host.Database.ReadStoredCommand(operation, cancellationToken));

    public Task<StoredCommand?> ReadLastCommittedCommandAsync(CancellationToken cancellationToken)
        => Task.FromResult(Isolated.Host.Database.ReadLastCommittedCommand(cancellationToken));

    public Task<StorageOperationResult> ApplyAsync(Command<DatabaseCommand> operation, long sequence, CancellationToken cancellationToken)
        => Task.FromResult(Isolated.Host.Database.ApplyStorageOperation(operation, sequence, cancellationToken));

    public Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken)
        => UsesSharedVolume ? Shared.ExecuteSharedAsync(signedRequest, null, cancellationToken)
            : Isolated.ExecuteAsync(signedRequest, null, (IGrainContext)this, cancellationToken);

    public Task AbortMaintenanceAsync(GrainReadKind kind, Guid sessionId)
        => UsesSharedVolume ? Shared.AbortSharedMaintenanceAsync(kind, sessionId) : Isolated.AbortMaintenanceAsync(kind, sessionId);

    public Task<GrainOperationReply> ExecuteCommandAsync(string signedRequest, string actorKey,
        CancellationToken cancellationToken)
        => UsesSharedVolume ? Shared.ExecuteSharedAsync(signedRequest, actorKey, cancellationToken)
            : Isolated.ExecuteAsync(signedRequest, actorKey, (IGrainContext)this, cancellationToken);

    public Task<NodeStatus> ReadStatusAsync(CancellationToken cancellationToken)
        => UsesSharedVolume ? Shared.ReadSharedStatusAsync(cancellationToken) : Isolated.ReadStatusAsync(cancellationToken);

    public Task<NodeAdmissionStatus> ReadAdmissionAsync(CancellationToken cancellationToken)
    {
        return UsesSharedVolume ? Shared.ReadSharedAdmissionAsync(cancellationToken) : Isolated.ReadAdmissionAsync(cancellationToken);
    }

    public Task<AdminNodeSnapshot> ReadDashboardAsync(CancellationToken cancellationToken)
        => UsesSharedVolume ? Shared.ReadSharedDashboardAsync(cancellationToken) : Isolated.ReadDashboardAsync(cancellationToken);

    public Task<BackupReceipt> CreateBackupAsync(CancellationToken cancellationToken)
        => UsesSharedVolume ? Shared.CreateSharedBackupAsync(cancellationToken) : Isolated.CreateBackupAsync(cancellationToken);

    /// <inheritdoc />
    public override async Task Init(IServiceProvider serviceProvider)
    {
        serviceProvider.GetRequiredService<ITelemetryHostInitializer>().Initialize();
        if (!UsesSharedVolume)
        {
            await owner.OpenAsync((IGrainContext)this).ConfigureAwait(true);
        }
        await serviceProvider.GetRequiredService<OrleansNode>().InitializeLocalRuntimeAsync(CancellationToken.None).ConfigureAwait(true);
    }
}
