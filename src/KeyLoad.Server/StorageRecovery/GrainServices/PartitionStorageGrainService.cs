using OpenTelemetry;
using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Server;

/// <summary>Opens this silo's one physical storage owner in the awaited native initialization phase.</summary>
/// <param name="grainId">The native service identity supplied by Orleans registration.</param>
/// <param name="silo">The actual owning silo, independent of logical partition activation placement.</param>
/// <param name="loggerFactory">The borrowed native logger factory.</param>
/// <param name="owner">The same silo-local owner retained through opening and eventual joined physical close.</param>
internal sealed class PartitionStorageGrainService(GrainId grainId, Silo silo, ILoggerFactory loggerFactory,
    PartitionHostApplicationOwner owner, IGrainFactory grains) : GrainService(grainId, silo, loggerFactory), ISiloDatabaseService
{
    private readonly PartitionHostApplicationOwner owner = owner;

    public Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = owner.Borrow().Database;
        return Task.FromResult(database.Store.Read(_ => new SiloStorageCut(database.Store.Identity.Incarnation, database.LastApplied)));
    }

    public Task<ReplicatedOperation> PrepareAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
        => Task.FromResult(owner.Borrow().Database.PrepareStorageOperation(operation, cancellationToken));

    public Task<StorageOperationResult> ApplyAsync(ReplicatedOperation operation, long sequence, CancellationToken cancellationToken)
        => Task.FromResult(owner.Borrow().Database.ApplyStorageOperation(operation, sequence, cancellationToken));

    public IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken)
        => grains.GetGrain<IRequestWorkerGrain>(RequestWorkerIdentity.ForRequests())
            .ExecuteStreamAsync(signedRequest, cancellationToken);

    public Task<GrainOperationReply> ExecuteCommandAsync(string signedRequest, string actorKey,
        CancellationToken cancellationToken)
        => grains.GetGrain<IRequestWorkerGrain>(RequestWorkerIdentity.ForCommands())
            .ExecuteCommandAsync(signedRequest, actorKey, cancellationToken);

    /// <inheritdoc />
    public override async Task Init(IServiceProvider serviceProvider)
    {
        serviceProvider.GetRequiredService<ITelemetryHostInitializer>().Initialize();
        await owner.OpenAsync().ConfigureAwait(true);
        await serviceProvider.GetRequiredService<OrleansNode>().InitializeLocalRuntimeAsync(CancellationToken.None).ConfigureAwait(true);
    }
}
