using KeyLoad.Clustering;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsLifetimeReadProbe
{
    internal static bool IsReadKind(GrainReadKind? kind)
        => kind is GrainReadKind.Document or GrainReadKind.BlobMetadata or GrainReadKind.BlobUploadInfo
            or GrainReadKind.BlobRange or GrainReadKind.BlobList or GrainReadKind.PartitionQuery or GrainReadKind.DistributedSearch
            or GrainReadKind.WaitForAnnIndex or GrainReadKind.ApproximateSearch;

    internal static bool ValidArm(RequestCqrsProbeArmRecord value)
        => ValidArm(value.Phase, value.Action, value.ReadKind, value.CommandId);

    internal static bool ValidArm(RequestCqrsProbePhase phase, RequestCqrsProbeAction action, GrainReadKind? readKind, Guid commandId)
        => action == RequestCqrsProbeAction.Hold && (readKind is null
            ? commandId != Guid.Empty
            : commandId == Guid.Empty && phase == RequestCqrsProbePhase.NativeModelAfterDispatch && IsReadKind(readKind));

    internal static bool Matches(RemoteResourceLifetimeReadKindV2 kind, GrainReadKind? read)
        => kind switch
        {
            RemoteResourceLifetimeReadKindV2.Document or RemoteResourceLifetimeReadKindV2.ControlledDocument => read == GrainReadKind.Document,
            RemoteResourceLifetimeReadKindV2.ControlledBlob => read is GrainReadKind.BlobMetadata or GrainReadKind.BlobUploadInfo
                or GrainReadKind.BlobRange or GrainReadKind.BlobList,
            RemoteResourceLifetimeReadKindV2.PartitionQuery => read == GrainReadKind.PartitionQuery,
            RemoteResourceLifetimeReadKindV2.DistributedSearch => read == GrainReadKind.DistributedSearch,
            _ => false
        };

    internal static void RequireReadWitness(RemoteModelNativeRequest request, Guid parentRequestId)
    {
        if (request.Channel == RemoteModelNativeChannel.ResourceLifetimeAnnRead)
        {
            RequestCqrsAnnLifetimeReadProbe.RequireReadWitness(request, parentRequestId);
            return;
        }
        var call = NativeSerialization.Deserialize<RemoteResourceLifetimeReadCallV2>(request.Payload.Span);
        var read = ExpectedReadKind(call);
        if (!MatchesCall(request, read, parentRequestId) || call.Payload.IsEmpty || !RemoteModelNativeIdentity.ValidNonce(call.Nonce))
        {
            throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidRecord);
        }
        RemoteResourceLifetimeReadValidation.RequireEnvelope(request, call);
    }

    private static GrainReadKind ExpectedReadKind(RemoteResourceLifetimeReadCallV2 call)
        => call.Kind switch
        {
            RemoteResourceLifetimeReadKindV2.Document or RemoteResourceLifetimeReadKindV2.ControlledDocument => GrainReadKind.Document,
            RemoteResourceLifetimeReadKindV2.ControlledBlob => BlobReadKind(call.Payload),
            RemoteResourceLifetimeReadKindV2.PartitionQuery => GrainReadKind.PartitionQuery,
            RemoteResourceLifetimeReadKindV2.DistributedSearch => GrainReadKind.DistributedSearch,
            _ => throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidRecord)
        };

    private static GrainReadKind BlobReadKind(ReadOnlyMemory<byte> payload)
    {
        var request = NativeSerialization.Deserialize<ControlledBlobReadRequestV2>(payload.Span);
        return request.Frame.Original.Purpose switch
        {
            ControlledBlobReadPurpose.Metadata => GrainReadKind.BlobMetadata,
            ControlledBlobReadPurpose.UploadInfo => GrainReadKind.BlobUploadInfo,
            ControlledBlobReadPurpose.Range => GrainReadKind.BlobRange,
            ControlledBlobReadPurpose.List => GrainReadKind.BlobList,
            _ => throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidRecord)
        };
    }

    internal static bool MatchesCall(RemoteModelNativeRequest original, GrainReadKind? read, Guid parentRequestId)
    {
        if (original.Channel == RemoteModelNativeChannel.ResourceLifetimeAnnRead)
        {
            return RequestCqrsAnnLifetimeReadProbe.MatchesCall(original, read, parentRequestId);
        }
        if (original.Channel != RemoteModelNativeChannel.ResourceLifetimeRead || !IsReadKind(read))
        {
            return false;
        }
        var call = NativeSerialization.Deserialize<RemoteResourceLifetimeReadCallV2>(original.Payload.Span);
        if (call.Version != RemoteResourceLifetimeReadProtocol.Version || call.RequestId == Guid.Empty
            || !Matches(call.Kind, read) || read != ExpectedReadKind(call) || call.ExpiresAt != original.ExpiresAt
            || call.CallerVoter != original.CallerVoter || call.CallerSiloAddress != original.CallerSiloAddress
            || call.Kind is not (RemoteResourceLifetimeReadKindV2.PartitionQuery or RemoteResourceLifetimeReadKindV2.DistributedSearch)
                && call.RequestId != parentRequestId)
        {
            throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidRecord);
        }
        return true;
    }
}
