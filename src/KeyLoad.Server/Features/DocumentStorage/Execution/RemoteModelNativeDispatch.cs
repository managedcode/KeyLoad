using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.EventStreams;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeDispatch
{
    internal static Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(
        RemoteDocumentRuntime runtime, RemoteModelNativeRequest request,
        RemoteModelNativeEffectBoundary? effectBoundary, CancellationToken token,
        IResourceLifetimeWorkBinding? binding)
    {
        if (request.Channel is RemoteModelNativeChannel.DocumentEnvelope or RemoteModelNativeChannel.ResourceLifetimeRead
            or RemoteModelNativeChannel.ResourceLifetimeBlobOutcome)
        {
            return (runtime.Endpoint ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteModelNativeProtocol.Unavailable))
            .ExecuteAsync(request, effectBoundary, token, binding);
        }
        if (request.Channel == RemoteModelNativeChannel.EventVector)
        {
            var call = NativeSerialization.Deserialize<EventVectorRemoteCall>(request.Payload.Span);
            if (!PhysicalOwnerEntryValidation.SameOwner(call.Source, request.SourceOwner)
                || call.ExpiresAt != request.ExpiresAt)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
            }
            return (runtime.EventVectors?.Endpoint ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteModelNativeProtocol.Unavailable))
                .ExecuteAsync(request.Payload, request.Signature, effectBoundary, token, RequireBinding(binding));
        }
        if (request.Channel == RemoteModelNativeChannel.ResourceLifetimeAnnRead)
        {
            return (runtime.AnnObservations ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteModelNativeProtocol.Unavailable))
                .ExecuteLifetimeAsync(request, token, RequireBinding(binding));
        }
        if (request.Channel == RemoteModelNativeChannel.AnnProjectionObservation)
        {
            var legacy = NativeSerialization.Deserialize<AnnProjectionNativeCall>(request.Payload.Span);
            if (legacy?.Observation?.Kind != AnnProjectionObservationKind.Identity)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
            }
            return (runtime.AnnObservations ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteModelNativeProtocol.Unavailable))
            .ExecuteAsync(request, token);
        }
        throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
    }

    internal static IResourceLifetimeWorkBinding RequireBinding(IResourceLifetimeWorkBinding? binding)
        => binding ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable);
}
