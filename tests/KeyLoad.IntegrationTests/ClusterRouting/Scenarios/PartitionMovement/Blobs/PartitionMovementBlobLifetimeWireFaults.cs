using KeyLoad.Clustering;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementBlobLifetimeWireFaults
{
    private const long EpochStep = 1;
    private const string WrongNonce = "wire-fault-nonce";
    private const string WrongResource = "wire-fault-resource";

    internal static IEnumerable<RemoteResourceLifetimeReadCallV2> Create(RemoteResourceLifetimeReadCallV2 original)
    {
        yield return original with
        {
            Nonce = WrongNonce
        };
        yield return original with
        {
            RequestId = Guid.NewGuid()
        };
        yield return original with
        {
            Source = original.Source with
            {
                PhysicalShardId = Guid.NewGuid()
            }
        };
        yield return original with
        {
            Destination = original.Destination with
            {
                Owner = original.Destination.Owner with
                {
                    Incarnation = Guid.NewGuid()
                }
            }
        };
        yield return original with
        {
            Kind = RemoteResourceLifetimeReadKindV2.Document
        };
        var request = NativeSerialization.Deserialize<ControlledBlobReadRequestV2>(original.Payload.Span);
        var frame = request.Frame.Original;
        foreach (var changed in Frames(frame))
        {
            yield return original with
            {
                Payload = NativeSerialization.Serialize(request with
                {
                    Frame = request.Frame with
                    {
                        Original = changed
                    }
                })
            };
        }
    }

    private static IEnumerable<ControlledBlobReadFrame> Frames(ControlledBlobReadFrame frame)
    {
        yield return frame with
        {
            Principal = frame.Principal with
            {
                PolicyEpoch = checked(frame.Principal.PolicyEpoch + EpochStep)
            }
        };
        yield return frame with
        {
            QueryId = Guid.NewGuid()
        };
        yield return frame with
        {
            Resource = frame.Resource with
            {
                Name = WrongResource
            }
        };
        yield return frame with
        {
            ExpiresAt = DateTimeOffset.MinValue
        };
        yield return frame with
        {
            Purpose = ControlledBlobReadPurpose.Outcome
        };
        yield return frame with
        {
            NativeRequest = frame.NativeRequest.IsEmpty ? new byte[] { 0x01 } : ReadOnlyMemory<byte>.Empty
        };
    }
}
