using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks the actual native capture's complete policy/catalog snapshot against literal seeded definitions.</summary>
internal static class ControlledPartitionMovementResourceAssertions
{
    internal static ImmutableArray<ResourceDefinition> Expected() =>
    [
        new(ControlledPartitionMovementCorpus.Collection, ResourceKind.Collection,
            ControlledPartitionMovementCorpus.Partition.TransactionDomainId),
        new(ControlledPartitionMovementCorpus.Topic, ResourceKind.Topic,
            ControlledPartitionMovementCorpus.Partition.TransactionDomainId),
        new(ControlledPartitionMovementCorpus.Queue, ResourceKind.WorkQueue,
            ControlledPartitionMovementCorpus.Partition.TransactionDomainId),
        new(ControlledPartitionMovementBlobSeed.Resource, ResourceKind.BlobStore,
            ControlledPartitionMovementCorpus.Partition.TransactionDomainId),
    ];

    internal static async Task CaptureAsync(PartitionMoveImageDescriptor actual)
    {
        var expected = Expected();
        await Assert.That(actual.Resources.Length).IsEqualTo(expected.Length);
        var original = actual.Resources.OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        var literal = expected.OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(original, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(literal, JsonDefaults.Options))).IsTrue();
        await Assert.That(actual.Partition).IsEqualTo(ControlledPartitionMovementCorpus.Partition);
    }
}
