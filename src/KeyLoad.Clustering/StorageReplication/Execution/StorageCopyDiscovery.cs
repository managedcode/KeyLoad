using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

/// <summary>Reads actual storage cuts through current Orleans membership without owning cluster state.</summary>
internal sealed class StorageCopyDiscovery(SiloDatabaseClient storage, IClusterMembershipService membership,
    Guid incarnation, ILogger diagnostics, PartitionRef? partition, StorageAccessMode mode)
{
    private const string SharedOwner = "shared-volume-owner";

    internal async Task<StorageCopyOwner[]> ReadAsync(bool allScopes, CancellationToken cancellationToken)
    {
        if (mode == StorageAccessMode.SharedVolume)
        {
            var owner = await storage.MetadataOwnerAsync(incarnation, storage.LocalSiloAddress, cancellationToken);
            var shared = await TryReadCutAsync(owner, allScopes, cancellationToken);
            return shared.Value is { } cut ? [new(owner, SharedOwner, cut)] : [];
        }
        var members = membership.CurrentSnapshot.Members.Values.Where(member => member.Status == SiloStatus.Active)
            .OrderBy(member => member.Name, StringComparer.Ordinal).ToArray();
        var reads = new Task<(bool HasValue, SiloStorageCut? Value)>[members.Length];
        for (var index = StorageCopyContract.FirstOwner; index < members.Length; index++)
        {
            reads[index] = TryReadCutAsync(members[index].SiloAddress, allScopes, cancellationToken);
        }
        var cuts = await Task.WhenAll(reads);
        var available = new List<StorageCopyOwner>(members.Length);
        for (var index = StorageCopyContract.FirstOwner; index < members.Length; index++)
        {
            if (cuts[index].Value is { } cut)
            {
                available.Add(new(members[index].SiloAddress, members[index].Name, cut));
            }
        }
        return [.. available];
    }

    private async Task<(bool HasValue, SiloStorageCut? Value)> TryReadCutAsync(SiloAddress silo, bool allScopes,
        CancellationToken cancellationToken)
    {
        try
        {
            var cut = allScopes ? await storage.On(silo).ReadCutAsync(cancellationToken)
                : await storage.On(silo).ReadPartitionCutAsync(partition, cancellationToken);
            if (cut.Incarnation == incarnation)
            {
                return (true, ((SiloStorageCut?)(cut)));
            }
            StorageCopyDiagnostics.CopyExcluded(diagnostics, silo, ErrorCode.RecoveryRequired);
            return (false, default(SiloStorageCut?));
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && CqrsBoundaryErrors.IsNonFatal(error))
        {
            StorageCopyDiagnostics.CopyExcluded(diagnostics, silo,
                error is KeyLoadException failure ? failure.Code : ErrorCode.OwnershipLost);
            return (false, default(SiloStorageCut?));
        }
    }

}
