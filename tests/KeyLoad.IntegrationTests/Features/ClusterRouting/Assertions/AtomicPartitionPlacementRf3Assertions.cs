using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class AtomicPartitionPlacementRf3Assertions
{
    internal static async Task<AtomicPartitionPlacementResolution> SdkReadAsync(
        Aspire.Hosting.DistributedApplication app, string node, string key, AtomicPartitionPlacementReadRequest request,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        var sdk = new KeyLoadClient(http, key, IntegrationClientOptions.Execution());
        var result = await sdk.ReadAtomicPartitionPlacementAsync(request, cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsSuccess).IsTrue();
        return result.Value!;
    }

    internal static async Task<AtomicPartitionPlacementResolution> McpReadAsync(
        McpOfficialClient client, AtomicPartitionPlacementReadRequest request, CancellationToken cancellationToken)
    {
        var response = await client.CallAsync(AtomicPartitionPlacementPublicRf3Protocol.ReadTool, request,
            cancellationToken).ConfigureAwait(false);
        var receipt = await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(response)
            .ConfigureAwait(false);
        return receipt.Value;
    }

    internal static async Task SameWitnessAsync(AtomicPartitionPlacementResolution expected,
        AtomicPartitionPlacementResolution actual)
    {
        await Assert.That(actual.Version).IsEqualTo(expected.Version);
        await Assert.That(actual.Partition).IsEqualTo(expected.Partition);
        await Assert.That(actual.PhysicalShardId).IsEqualTo(expected.PhysicalShardId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.VoterIds.SequenceEqual(expected.VoterIds, StringComparer.Ordinal)).IsTrue();
        await Assert.That(actual.PlacementEpoch).IsEqualTo(expected.PlacementEpoch);
        await Assert.That(actual.DirectoryRevision).IsEqualTo(expected.DirectoryRevision);
        await Assert.That(actual.Revision).IsEqualTo(expected.Revision);
        await Assert.That(actual.IsFallback).IsEqualTo(expected.IsFallback);
    }

    internal static async Task SameOwnerAsync(AtomicPartitionPlacementResolution expected,
        AtomicPartitionPlacementResolution actual)
    {
        await Assert.That(actual.PhysicalShardId).IsEqualTo(expected.PhysicalShardId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.VoterIds.SequenceEqual(expected.VoterIds, StringComparer.Ordinal)).IsTrue();
        await Assert.That(actual.PlacementEpoch).IsEqualTo(expected.PlacementEpoch);
    }

    internal static async Task ValidOwnerAsync(AtomicPartitionPlacementResolution actual, Guid shardId)
    {
        await Assert.That(actual.PhysicalShardId).IsEqualTo(shardId);
        await Assert.That(actual.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.VoterIds.Length).IsEqualTo(PhysicalShardCatalogRf3Protocol.VoterCount);
        await Assert.That(actual.VoterIds.Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(PhysicalShardCatalogRf3Protocol.VoterCount);
    }
}
