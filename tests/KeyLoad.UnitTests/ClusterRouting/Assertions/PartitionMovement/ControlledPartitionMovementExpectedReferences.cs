using System.Text.Json;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Derives complete literal entity identities from the original resource-creation command.</summary>
internal static class ControlledPartitionMovementExpectedReferences
{
    private const long CollectionIndex = 3;
    private const string MissingCreation = "The original movement collection creation command is absent.";
    private static readonly Guid CollectionCommandId = Guid.Parse("bde5e9ef-a97c-46b2-baf8-1c09f4ce0c30");

    internal static async Task<EntityRef> DocumentAsync(ControlledPartitionMovementNode source)
    {
        var original = source.Journal.Log.ReadEntry(CollectionIndex)?.Operation
            ?? throw new InvalidOperationException(MissingCreation);
        await Assert.That(original.Id).IsEqualTo(CollectionCommandId);
        await Assert.That(original.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await Assert.That(original.PrincipalId).IsEqualTo(PhysicalShardCatalogFixture.RootPrincipalId);
        var partition = ControlledPartitionMovementCorpus.Partition;
        var definition = new ResourceDefinition(ControlledPartitionMovementCorpus.Collection,
            ResourceKind.Collection, partition.TransactionDomainId);
        var request = NativeCommandPayload.Read<ConfigureResourceRequest>(original);
        var expected = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(request, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options))).IsTrue();
        var lifetime = ResourceLifetimeIdentity.Create(original, partition.TenantId, partition.DatabaseId, definition);
        return new EntityRef(partition, ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.DocumentId)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
    }
}
