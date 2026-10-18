using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime.Services;

namespace KeyLoad.Clustering;

/// <summary>Addresses storage services using native Orleans membership addresses.</summary>
public sealed class SiloDatabaseClient(IServiceProvider services) : GrainServiceClient<ISiloDatabaseService>(services)
{
    private readonly IGrainFactory grains = services.GetRequiredService<IGrainFactory>();
    private readonly ILocalSiloDetails local = services.GetRequiredService<ILocalSiloDetails>();

    /// <summary>The current native silo generation, independent of persisted physical storage identity.</summary>
    public SiloAddress LocalSiloAddress => local.SiloAddress;

    /// <summary>Borrows the storage service on the exact native silo generation.</summary>
    public ISiloDatabaseService On(SiloAddress address) => GetGrainService(address);

    /// <summary>Resolves the actual available canonical store through its ordinary database grain.</summary>
    public Task<SiloAddress> ReadOwnerAsync(Guid incarnation, CancellationToken cancellationToken)
        => grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(incarnation))
            .GetReadOwnerAsync(local.SiloAddress, cancellationToken);

    /// <summary>Awaits one self-contained Task leaf at the selected storage owner.</summary>
    public Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, string signedRequest, CancellationToken cancellationToken)
        => grains.GetGrain<IRequestWorkerGrain>(RequestWorkerIdentity.ForRequests())
            .ExecuteAsync(owner, signedRequest, cancellationToken);

    /// <summary>Joins the selected owner's original native maintenance cleanup after the parent stops.</summary>
    public Task AbortMaintenanceAsync(SiloAddress owner, GrainReadKind kind, Guid sessionId)
        => On(owner).AbortMaintenanceAsync(kind, sessionId);

    /// <summary>Resolves storage through Orleans, keeping partition activation placement separate from open files.</summary>
    public async Task<GrainOperationReply> ExecuteCommandAsync(Guid incarnation, string signedRequest,
        string actorKey, CancellationToken cancellationToken)
    {
        var owner = await ReadOwnerAsync(incarnation, cancellationToken).ConfigureAwait(true);
        return await grains.GetGrain<IRequestWorkerGrain>(RequestWorkerIdentity.ForRequests())
            .ExecuteCommandAsync(owner, signedRequest, actorKey, cancellationToken).ConfigureAwait(true);
    }
}
