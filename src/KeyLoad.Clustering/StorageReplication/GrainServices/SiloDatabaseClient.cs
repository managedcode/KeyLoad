using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime.Services;

namespace KeyLoad.Clustering;

/// <summary>Addresses storage services using execution Orleans membership addresses.</summary>
public sealed class SiloDatabaseClient(IServiceProvider services) : GrainServiceClient<ISiloDatabaseService>(services)
{
    private readonly IGrainFactory grains = services.GetRequiredService<IGrainFactory>();
    private readonly ILocalSiloDetails local = services.GetRequiredService<ILocalSiloDetails>();
    private readonly IClusterMembershipService membership = services.GetRequiredService<IClusterMembershipService>();
    private readonly IOptions<ReplicaCopyOptions> copies = services.GetRequiredService<IOptions<ReplicaCopyOptions>>();

    /// <summary>The current execution silo generation, independent of persisted physical storage identity.</summary>
    public SiloAddress LocalSiloAddress => local.SiloAddress;

    /// <summary>Borrows the storage service on the exact execution silo generation.</summary>
    public ISiloDatabaseService On(SiloAddress address) => GetGrainService(address);

    /// <summary>Resolves the actual available canonical store through its ordinary database grain.</summary>
    public Task<SiloAddress> ReadOwnerAsync(Guid incarnation, CancellationToken cancellationToken)
        => grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(incarnation))
            .GetReadOwnerAsync(local.SiloAddress, cancellationToken);

    /// <summary>Routes requests directly to the only isolated owner; otherwise resolves the canonical copy.</summary>
    internal ValueTask<SiloAddress> RequestOwnerAsync(Guid incarnation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var address = local.SiloAddress;
        if (copies.Value is { StorageMode: StorageAccessMode.IsolatedVolumes, SiloCount: StorageCopyContract.MinimumCount })
        {
            var members = membership.CurrentSnapshot.Members;
            if (members.Count == StorageCopyContract.MinimumCount
                && members.TryGetValue(address, out var member) && member.Status == SiloStatus.Active)
            {
                // Execution still borrows the published physical session and validates the original request there.
                return ValueTask.FromResult(address);
            }
        }
        return new(ReadOwnerAsync(incarnation, cancellationToken));
    }

    /// <summary>Places an independent request activation beside the selected physical owner and awaits its original work.</summary>
    public async Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, GrainRequestEnvelope envelope,
        CancellationToken cancellationToken, string? actorKey = null)
    {
        ArgumentNullException.ThrowIfNull(envelope) /* KLD0041: BCL null validation accepts the envelope GrainRequestEnvelope reference through its object parameter. */;
        using var placement = GrainRequestIdentityScope.ForPlacement(envelope, owner, cancellationToken);
        var request = grains.GetGrain<IRequestExecutionGrain>(RequestExecutionGrainIdentity.For(envelope.RequestId));
        return await request.ExecuteAsync(owner, envelope, actorKey, cancellationToken);
    }

    /// <summary>Joins the selected owner's original execution maintenance cleanup after the parent stops.</summary>
    public Task AbortMaintenanceAsync(SiloAddress owner, GrainReadKind kind, Guid sessionId)
        => On(owner).AbortMaintenanceAsync(kind, sessionId);

}
