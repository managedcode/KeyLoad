using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementNativeBlobMutationAssertions
{
    internal static void Require(RemoteControlledBlobCall original, NativeControlledBlobFaultPayload fault)
    {
        var actual = NativeSerialization.Deserialize<RemoteDocumentTransportEnvelope>(fault.Payload.Span);
        var expected = fault.Variation == NativeControlledBlobFaultVariation.MissingEnvelope
            ? new RemoteDocumentTransportEnvelope(null, null, null)
            : new RemoteDocumentTransportEnvelope(null, null, Change(original,
                actual.ControlledBlob ?? throw Missing(), fault.Variation));
        if (!RemoteDocumentWire.Encode(expected).AsSpan().SequenceEqual(fault.Payload.Span))
        { throw Missing(); }
    }

    private static RemoteControlledBlobCall Change(RemoteControlledBlobCall original, RemoteControlledBlobCall actual,
        NativeControlledBlobFaultVariation variation)
    {
        var frame = original.Request.Frame;
        var changed = actual.Request.Frame;
        return variation switch
        {
            NativeControlledBlobFaultVariation.Nonce => original with { Nonce = "wire-fault-nonce" },
            NativeControlledBlobFaultVariation.RequestId => original with { RequestId = Different(actual.RequestId, original.RequestId) },
            NativeControlledBlobFaultVariation.SourcePhysicalShard => original with
            { Source = original.Source with { PhysicalShardId = Different(actual.Source.PhysicalShardId, original.Source.PhysicalShardId) } },
            NativeControlledBlobFaultVariation.DestinationIncarnation => original with
            {
                Destination = original.Destination with
                {
                    Owner = original.Destination.Owner with
                    { Incarnation = Different(actual.Destination.Owner.Incarnation, original.Destination.Owner.Incarnation) }
                }
            },
            NativeControlledBlobFaultVariation.PrincipalPolicyEpoch => ChangePrincipalPolicyEpoch(original, frame),
            NativeControlledBlobFaultVariation.QueryId => original with
            { Request = original.Request with { Frame = frame with { QueryId = Different(changed.QueryId, frame.QueryId) } } },
            NativeControlledBlobFaultVariation.Resource => original with
            { Request = original.Request with { Frame = frame with { Resource = frame.Resource with { Name = "wire-fault-resource" } } } },
            NativeControlledBlobFaultVariation.ExpiresAt => original with
            { Request = original.Request with { Frame = frame with { ExpiresAt = DateTimeOffset.MinValue } } },
            NativeControlledBlobFaultVariation.Purpose => original with
            {
                Request = original.Request with
                {
                    Frame = frame with
                    {
                        Purpose = frame.Purpose == ControlledBlobReadPurpose.Outcome
                ? ControlledBlobReadPurpose.Metadata : ControlledBlobReadPurpose.Outcome
                    }
                }
            },
            NativeControlledBlobFaultVariation.NativeRequest => original with
            {
                Request = original.Request with
                {
                    Frame = frame with
                    { NativeRequest = frame.NativeRequest.IsEmpty ? new byte[] { 0x01 } : ReadOnlyMemory<byte>.Empty }
                }
            },
            NativeControlledBlobFaultVariation.OriginalOutcome when frame.OriginalOutcome is not null => original with
            { Request = original.Request with { Frame = frame with { OriginalOutcome = null } } },
            NativeControlledBlobFaultVariation.OriginalOperationId when frame.Original is { } operation => original with
            {
                Request = original.Request with
                {
                    Frame = frame with
                    {
                        Original = operation with
                        { Id = Different(changed.Original?.Id ?? throw Missing(), operation.Id) }
                    }
                }
            },
            _ => throw Missing()
        };
    }

    private static RemoteControlledBlobCall ChangePrincipalPolicyEpoch(RemoteControlledBlobCall original, ControlledBlobReadFrame frame)
    {
        return original with
        {
            Request = original.Request with
            {
                Frame = frame with
                { Principal = frame.Principal with { PolicyEpoch = checked(frame.Principal.PolicyEpoch + 1) } }
            }
        };
    }

    private static Guid Different(Guid actual, Guid original)
        => actual != Guid.Empty && actual != original ? actual : throw Missing();

    private static InvalidOperationException Missing() => new("The original native blob wire mutation proof is invalid.");
}
