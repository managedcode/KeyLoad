using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class ControlledBlobLifetimeReadCalls
{
    private const long FirstOffset = 0;

    internal static readonly GrainReadKind[] Kinds =
        [GrainReadKind.BlobMetadata, GrainReadKind.BlobUploadInfo, GrainReadKind.BlobRange, GrainReadKind.BlobList];

    internal static Func<CancellationToken, Task<ControlledBlobLifetimeReadObservation>> For(
        PartitionMovementPublicParentRf3Seed seed, GrainReadKind kind)
        => kind switch
        {
            GrainReadKind.BlobMetadata => token => ObserveAsync(seed.Source.GetBlobMetadataAsync(new(seed.Blob.Blob), token)),
            GrainReadKind.BlobUploadInfo => token => ObserveAsync(seed.Source.GetBlobUploadInfoAsync(
                new(seed.Blob.Blob, seed.BlobOriginals.Begin.UploadId), token)),
            GrainReadKind.BlobRange => token => ObserveAsync(seed.Source.ReadBlobRangeAsync(new(seed.Blob.Blob,
                seed.Blob.Revision, FirstOffset, PartitionMovementPublicParentRf3Seed.BlobRange.Length), token)),
            GrainReadKind.BlobList => token => ObserveAsync(seed.Source.ListBlobsAsync(
                new(seed.Partition, seed.Blob.Blob.Resource, BlobLimits.MaxListItems), token)),
            _ => throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidArm)
        };

    private static async Task<ControlledBlobLifetimeReadObservation> ObserveAsync<T>(Task<Result<T>> original)
    {
        var result = await original.ConfigureAwait(false);
        return new(result.IsFailed, result.Value, result.Problem?.ErrorCode);
    }

    internal static async Task RequireFailureAsync(ControlledBlobLifetimeReadObservation result, ErrorCode? expected)
    {
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Value).IsNull();
        if (expected is { } code)
        {
            await Assert.That(result.Code).IsEqualTo(code.ToString());
        }
        else
        {
            await Assert.That(string.IsNullOrWhiteSpace(result.Code)).IsFalse();
        }
    }
}
