using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime.Services;

namespace KeyLoad.Clustering;

/// <summary>Addresses storage services using execution Orleans membership addresses.</summary>
public sealed class SiloDatabaseClient(IServiceProvider services) : GrainServiceClient<ISiloDatabaseService>(services)
{
    private readonly IGrainFactory grains = services.GetRequiredService<IGrainFactory>();
    private readonly ILocalSiloDetails local = services.GetRequiredService<ILocalSiloDetails>();
    private readonly IServiceProvider runtimeServices = services;

    /// <summary>The current execution silo generation, independent of persisted physical storage identity.</summary>
    public SiloAddress LocalSiloAddress => local.SiloAddress;

    /// <summary>Borrows the storage service on the exact execution silo generation.</summary>
    public ISiloDatabaseService On(SiloAddress address) => GetGrainService(address);

    /// <summary>Resolves the actual available canonical store through its ordinary database grain.</summary>
    public Task<SiloAddress> ReadOwnerAsync(Guid incarnation, CancellationToken cancellationToken)
        => grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(incarnation))
            .GetReadOwnerAsync(local.SiloAddress, cancellationToken);

    /// <summary>Places an independent request activation beside the selected physical owner and awaits its original work.</summary>
    public async Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, GrainRequestEnvelope envelope,
        CancellationToken cancellationToken, string? actorKey = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        using var placement = GrainRequestIdentityScope.ForPlacement(runtimeServices, envelope, owner, cancellationToken);
        var request = grains.GetGrain<IRequestExecutionGrain>(RequestExecutionGrainIdentity.For(envelope.RequestId));
        return await request.ExecuteAsync(owner, envelope, actorKey, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Joins the selected owner's original execution maintenance cleanup after the parent stops.</summary>
    public Task AbortMaintenanceAsync(SiloAddress owner, GrainReadKind kind, Guid sessionId)
        => On(owner).AbortMaintenanceAsync(kind, sessionId);

}
