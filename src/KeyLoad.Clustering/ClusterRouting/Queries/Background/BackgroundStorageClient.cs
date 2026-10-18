using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Addresses native storage owners without opening files in a worker or durable coordinator.</summary>
internal sealed class BackgroundStorageClient(GrainRequestCodec codec, SiloDatabaseClient storage,
    IClusterMembershipService membership, IGrainFactory grains, IOptions<ReplicaCopyOptions> copies)
{
    internal IBackgroundStorageOperations Operations
        => grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(codec.Incarnation));

    internal Task<SiloAddress> AnnDiscoveryTargetAsync(CancellationToken token)
        => copies.Value.StorageMode == StorageAccessMode.SharedVolume
            ? storage.ReadOwnerAsync(codec.Incarnation, token) : Task.FromResult(storage.LocalSiloAddress);

    internal async Task<SiloAddress> ResolveAnnOwnerAsync(Guid nodeId, CancellationToken token)
    {
        if (nodeId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        }
        if (copies.Value.StorageMode == StorageAccessMode.SharedVolume)
        {
            return await storage.ReadOwnerAsync(codec.Incarnation, token).ConfigureAwait(true);
        }
        foreach (var member in membership.CurrentSnapshot.Members.Values
            .Where(member => member.Status == SiloStatus.Active).OrderBy(member => member.Name, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var status = await storage.On(member.SiloAddress).ReadStatusAsync(token).ConfigureAwait(true);
            if (status.Incarnation != codec.Incarnation || !Guid.TryParse(status.NodeId, out var actual))
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid);
            }
            if (actual == nodeId)
            {
                return member.SiloAddress;
            }
        }
        throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
    }
}
