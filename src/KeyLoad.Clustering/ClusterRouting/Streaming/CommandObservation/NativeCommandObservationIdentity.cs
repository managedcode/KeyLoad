using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class NativeCommandObservationIdentity
{
    private const int NoCapacity = 0;
    internal static void RequireAdmission(DecodedGrainRequest verified, NativeCommandObservationAdmissionV1 admission,
        DatabaseEngine database)
    {
        var envelope = verified.Envelope;
        var limits = database.Limits;
        if (!NativeCommandObservationScope.IsEligible(verified) || admission is null
            || !Same(admission, envelope.Observation) || admission.Version != NativeCommandObservationProtocol.CurrentVersion
            || admission.ObservationId == Guid.Empty || admission.ObservationId == envelope.RequestId
            || admission.MaximumReadBytes <= NoCapacity || admission.MaximumReadBytes > limits.MaxQueryReadBytes
            || admission.MaximumExaminedRecords <= NoCapacity || admission.MaximumExaminedRecords > limits.MaxScanRecords
            || admission.MaximumResultBytes <= NoCapacity || admission.MaximumResultBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, NativeRequestWorkLimits.InvalidIdentityMessage);
        }
    }

    internal static void RequireIntent(NativeCommandObservationHandleV1 handle, ReplicatedOperation intent,
        DatabaseEngine database)
    {
        if (intent is null || intent.Id != handle.CommandId || intent.Kind != handle.Kind
            || intent.PrincipalId != handle.PrincipalId || intent.Id == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        _ = database.VerifyOperationAuthority(intent);
    }

    internal static NativeCommandObservationHandleV1 Create(DecodedGrainRequest verified,
        PhysicalShardRecord actualOwner, SiloStorageIdentity actualRuntime, DatabaseEngine database)
    {
        var request = verified.Envelope;
        var identity = database.Store.Identity;
        return new(NativeCommandObservationProtocol.CurrentVersion, NativeCommandObservationProtocol.Purpose,
            request.Observation!.ObservationId, request.RequestId, request.CommandId, request.CommandKind!.Value,
            request.PrincipalId!, request.Incarnation, actualOwner, actualRuntime, identity.NodeId,
            identity.ReadGeneration, request.ExpiresAt, SHA256.HashData(verified.Payload.Span));
    }

    internal static void Require(NativeCommandObservationHandleV1 supplied, NativeCommandObservationHandleV1 original,
        PhysicalShardRecord actualOwner, SiloStorageIdentity actualRuntime, DatabaseEngine database)
    {
        var identity = database.Store.Identity;
        if (!Same(supplied, original) || !Same(supplied.PhysicalOwner, actualOwner)
            || !Same(supplied.Runtime, actualRuntime) || identity.NodeId != supplied.NodeId
            || identity.Incarnation != supplied.Incarnation || identity.ReadGeneration != supplied.ReadGeneration)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }

    internal static void ClearRequest(DecodedGrainRequest request)
    {
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(request.Payload, out var segment)
            && segment.Array is not null)
        {
            CryptographicOperations.ZeroMemory(segment.AsSpan());
        }
    }

    private static bool Same<T>(T left, T right)
        => NativeSerialization.Serialize(left).AsSpan().SequenceEqual(NativeSerialization.Serialize(right));
}
