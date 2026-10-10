using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementBlobWireFaults
{
    private const long EpochStep = 1;
    private const string WrongNonce = "wire-fault-nonce";
    private const string WrongResource = "wire-fault-resource";

    internal static IEnumerable<RemoteDocumentTransportEnvelope> Create(RemoteControlledBlobCall original)
    {
        yield return Wrap(original with { Nonce = WrongNonce });
        yield return Wrap(original with { RequestId = Guid.NewGuid() });
        yield return Wrap(original with { Source = original.Source with { PhysicalShardId = Guid.NewGuid() } });
        yield return Wrap(original with
        {
            Destination = original.Destination with
            { Owner = original.Destination.Owner with { Incarnation = Guid.NewGuid() } }
        });
        yield return new RemoteDocumentTransportEnvelope(null, null, null);
        var frame = original.Request.Frame;
        yield return Wrap(original with
        {
            Request = original.Request with
            {
                Frame = frame with
                { Principal = frame.Principal with { PolicyEpoch = checked(frame.Principal.PolicyEpoch + EpochStep) } }
            }
        });
        yield return Wrap(original with { Request = original.Request with { Frame = frame with { QueryId = Guid.NewGuid() } } });
        yield return Wrap(original with
        {
            Request = original.Request with
            {
                Frame = frame with
                { Resource = frame.Resource with { Name = WrongResource } }
            }
        });
        yield return Wrap(original with { Request = original.Request with { Frame = frame with { ExpiresAt = DateTimeOffset.MinValue } } });
        yield return Wrap(original with
        {
            Request = original.Request with
            {
                Frame = frame with
                { Purpose = frame.Purpose == ControlledBlobReadPurpose.Outcome ? ControlledBlobReadPurpose.Metadata : ControlledBlobReadPurpose.Outcome }
            }
        });
        yield return Wrap(original with
        {
            Request = original.Request with
            {
                Frame = frame with
                { NativeRequest = frame.NativeRequest.IsEmpty ? new byte[] { 0x01 } : ReadOnlyMemory<byte>.Empty }
            }
        });
        if (frame.OriginalOutcome is not null)
        { yield return Wrap(original with { Request = original.Request with { Frame = frame with { OriginalOutcome = null } } }); }
        if (frame.Original is { } operation)
        { yield return Wrap(original with { Request = original.Request with { Frame = frame with { Original = operation with { Id = Guid.NewGuid() } } } }); }
    }

    private static RemoteDocumentTransportEnvelope Wrap(RemoteControlledBlobCall call) => new(null, null, call);
}
