using KeyLoad.Clustering;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeControlledBlobLifetimeFaultCatalog
{
    private const string WrongNonce = "wire-fault-nonce";
    private const string WrongResource = "wire-fault-resource";
    private const long EpochStep = 1;

    internal static IEnumerable<NativeControlledBlobFaultPayload> Create(RemoteResourceLifetimeReadCallV2 original,
        string signature, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        var request = NativeControlledBlobLifetimeCall.Require(original);
        foreach (var payload in EnvelopeFaults(original, signature, token))
        {
            yield return payload;
        }
        foreach (var payload in FrameFaults(original, request, signature, token))
        {
            yield return payload;
        }
    }

    private static IEnumerable<NativeControlledBlobFaultPayload> EnvelopeFaults(RemoteResourceLifetimeReadCallV2 original,
        string signature, CancellationToken token)
    {
        yield return Wrap(NativeControlledBlobFaultVariation.Nonce, original with
        {
            Nonce = WrongNonce
        }, signature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.RequestId, original with
        {
            RequestId = Guid.NewGuid()
        }, signature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.SourcePhysicalShard, original with
        {
            Source = original.Source with
            {
                PhysicalShardId = Guid.NewGuid()
            }
        }, signature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.DestinationIncarnation, original with
        {
            Destination = original.Destination with
            {
                Owner = original.Destination.Owner with
                {
                    Incarnation = Guid.NewGuid()
                }
            }
        }, signature, token);
        // V2 has one mandatory typed model payload, with no V1 envelope fallback.
        yield return Wrap(NativeControlledBlobFaultVariation.MissingEnvelope,
            original with
            {
                Payload = ReadOnlyMemory<byte>.Empty
            }, signature, token);
    }

    private static IEnumerable<NativeControlledBlobFaultPayload> FrameFaults(RemoteResourceLifetimeReadCallV2 original,
        ControlledBlobReadRequestV2 request, string signature, CancellationToken token)
    {
        var frame = request.Frame.Original;
        yield return Wrap(NativeControlledBlobFaultVariation.PrincipalPolicyEpoch, WithFrame(original, request,
            frame with
            {
                Principal = frame.Principal with
                {
                    PolicyEpoch = checked(frame.Principal.PolicyEpoch + EpochStep)
                }
            }), signature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.QueryId,
            WithFrame(original, request, frame with
            {
                QueryId = Guid.NewGuid()
            }), signature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.Resource, WithFrame(original, request,
            frame with
            {
                Resource = frame.Resource with
                {
                    Name = WrongResource
                }
            }), signature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.ExpiresAt,
            WithFrame(original, request, frame with
            {
                ExpiresAt = DateTimeOffset.MinValue
            }), signature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.Purpose,
            WithFrame(original, request, frame with
            {
                Purpose = ControlledBlobReadPurpose.Outcome
            }), signature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.NativeRequest,
            WithFrame(original, request, frame with
            {
                NativeRequest = ReadOnlyMemory<byte>.Empty
            }), signature, token);
    }

    private static RemoteResourceLifetimeReadCallV2 WithFrame(RemoteResourceLifetimeReadCallV2 call,
        ControlledBlobReadRequestV2 request, ControlledBlobReadFrame frame)
    {
        var changed = request with
        {
            Frame = request.Frame with
            {
                Original = frame
            }
        };
        return call with
        {
            Payload = NativeSerialization.Serialize(changed)
        };
    }

    private static NativeControlledBlobFaultPayload Wrap(NativeControlledBlobFaultVariation variation,
        RemoteResourceLifetimeReadCallV2 call, string signature, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new(variation, RemoteDocumentWire.Encode(call), signature);
    }
}
