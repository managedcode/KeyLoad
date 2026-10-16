using KeyLoad.Clustering;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementNativeBlobLifetimeMutationAssertions
{
    private const string WrongNonce = "wire-fault-nonce";
    private const string WrongResource = "wire-fault-resource";
    private const long EpochStep = 1;
    private const string MissingProof = "The original V2 native blob mutation proof is invalid.";

    internal static void Require(RemoteResourceLifetimeReadCallV2 original, NativeControlledBlobFaultPayload fault)
    {
        var actual = NativeSerialization.Deserialize<RemoteResourceLifetimeReadCallV2>(fault.Payload.Span);
        var request = NativeControlledBlobLifetimeCall.Require(original);
        var expected = fault.Variation switch
        {
            NativeControlledBlobFaultVariation.Nonce => original with { Nonce = WrongNonce },
            NativeControlledBlobFaultVariation.RequestId => original with { RequestId = Different(actual.RequestId, original.RequestId) },
            NativeControlledBlobFaultVariation.SourcePhysicalShard => original with
            {
                Source = original.Source with
                {
                    PhysicalShardId = Different(actual.Source.PhysicalShardId, original.Source.PhysicalShardId)
                }
            },
            NativeControlledBlobFaultVariation.DestinationIncarnation => original with
            {
                Destination = original.Destination with
                {
                    Owner = original.Destination.Owner with
                    {
                        Incarnation = Different(actual.Destination.Owner.Incarnation, original.Destination.Owner.Incarnation)
                    }
                }
            },
            NativeControlledBlobFaultVariation.MissingEnvelope => original with { Payload = ReadOnlyMemory<byte>.Empty },
            _ => ChangeFrame(original, request, actual, fault.Variation)
        };
        if (!RemoteDocumentWire.Encode(expected).AsSpan().SequenceEqual(fault.Payload.Span))
        {
            throw Missing();
        }
    }

    private static RemoteResourceLifetimeReadCallV2 ChangeFrame(RemoteResourceLifetimeReadCallV2 original,
        ControlledBlobReadRequestV2 request, RemoteResourceLifetimeReadCallV2 actual, NativeControlledBlobFaultVariation variation)
    {
        var frame = request.Frame.Original;
        var changed = NativeSerialization.Deserialize<ControlledBlobReadRequestV2>(actual.Payload.Span).Frame.Original;
        var expected = variation switch
        {
            NativeControlledBlobFaultVariation.PrincipalPolicyEpoch => frame with
            {
                Principal = frame.Principal with
                {
                    PolicyEpoch = checked(frame.Principal.PolicyEpoch + EpochStep)
                }
            },
            NativeControlledBlobFaultVariation.QueryId => frame with { QueryId = Different(changed.QueryId, frame.QueryId) },
            NativeControlledBlobFaultVariation.Resource => frame with { Resource = frame.Resource with { Name = WrongResource } },
            NativeControlledBlobFaultVariation.ExpiresAt => frame with { ExpiresAt = DateTimeOffset.MinValue },
            NativeControlledBlobFaultVariation.Purpose => frame with { Purpose = ControlledBlobReadPurpose.Outcome },
            NativeControlledBlobFaultVariation.NativeRequest => frame with { NativeRequest = ReadOnlyMemory<byte>.Empty },
            _ => throw Missing()
        };
        var payload = NativeSerialization.Serialize(request with
        {
            Frame = request.Frame with
            {
                Original = expected
            }
        });
        return original with
        {
            Payload = payload
        };
    }

    private static Guid Different(Guid actual, Guid original)
        => actual != Guid.Empty && actual != original ? actual : throw Missing();
    private static InvalidOperationException Missing() => new(MissingProof);
}
