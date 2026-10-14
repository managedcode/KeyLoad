using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.EventStreams;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeDispatch
{
    internal static Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(
        RemoteDocumentRuntime runtime, RemoteModelNativeRequest request,
        RemoteModelNativeEffectBoundary? effectBoundary, CancellationToken token)
    {
        if (request.Channel == RemoteModelNativeChannel.DocumentEnvelope)
        {
            return (runtime.Endpoint ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteModelNativeProtocol.Unavailable))
            .ExecuteAsync(request, effectBoundary, token);
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
                .ExecuteAsync(request.Payload, request.Signature, effectBoundary, token);
        }
        if (request.Channel == RemoteModelNativeChannel.AnnProjectionObservation)
        {
            return (runtime.AnnObservations ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteModelNativeProtocol.Unavailable))
            .ExecuteAsync(request, token);
        }
        throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
    }
}
