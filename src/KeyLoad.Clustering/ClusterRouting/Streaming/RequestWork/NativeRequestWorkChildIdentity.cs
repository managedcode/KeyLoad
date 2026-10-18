using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.Clustering;

/// <summary>Bounded local fingerprint of a verified signed child and its expected actual execution owner.</summary>
internal sealed record NativeRequestWorkChildIdentity(Guid RequestId, ImmutableArray<byte> Fingerprint)
{
    internal static bool IsSelectedPurpose(string? purpose)
        => purpose is QueryLifetimeReadProtocol.Purpose
            or AnnProjectionLifetimeObservationScope.Purpose
            or AnnMaintenanceChildScope.Purpose or AnnProjectionJobChildScope.Purpose
            or TextMaintenanceChildScope.Purpose or OnlineTextRequestScope.Purpose or MultiLaneReceiveChildScope.Purpose;

    internal static bool IsSelected(DecodedGrainRequest request)
        => request.Envelope.Purpose switch
        {
            QueryLifetimeReadProtocol.Purpose => request.Envelope.ReadKind is
                GrainReadKind.PartitionQueryLeafV2 or GrainReadKind.DistributedSearchLeafV2,
            AnnProjectionLifetimeObservationScope.Purpose =>
                request.Envelope.ReadKind == GrainReadKind.AnnProjectionObservationV2,
            AnnMaintenanceChildScope.Purpose => AnnMaintenanceChildScope.IsSelected(request.Envelope),
            AnnProjectionJobChildScope.Purpose => AnnProjectionJobChildScope.IsSelected(request.Envelope),
            TextMaintenanceChildScope.Purpose => TextMaintenanceChildScope.IsSelected(request.Envelope),
            OnlineTextRequestScope.Purpose => OnlineTextRequestScope.IsSelected(request.Envelope),
            MultiLaneReceiveChildScope.Purpose => MultiLaneReceiveChildScope.IsSelected(request.Envelope),
            _ => false
        };

    internal static NativeRequestWorkChildIdentity Create(DecodedGrainRequest verified,
        PhysicalShardRecord actualOwner, SiloStorageIdentity runtime)
    {
        if (!IsSelected(verified) || verified.Envelope.RequestId == Guid.Empty
            || string.IsNullOrEmpty(verified.Envelope.PrincipalId)
            || verified.Envelope.Incarnation != actualOwner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(NativeSerialization.Serialize(verified.Envelope));
        hash.AppendData(verified.Payload.Span);
        hash.AppendData(NativeSerialization.Serialize(actualOwner));
        hash.AppendData(NativeSerialization.Serialize(runtime));
        return new(verified.Envelope.RequestId, ImmutableArray.CreateRange(hash.GetHashAndReset()));
    }
}
