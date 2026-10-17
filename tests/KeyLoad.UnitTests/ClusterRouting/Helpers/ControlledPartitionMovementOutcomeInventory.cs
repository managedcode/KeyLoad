using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementOutcomeInventory
{
    private const string Missing = "The original native seed entry or outcome is absent.";
    internal const long CollectionIndex = 3;
    internal const long TopicIndex = 4;
    internal const long QueueIndex = 5;
    internal const long BatchIndex = 6;
    internal const long BlobResourceIndex = 7;
    internal const long BeginIndex = 8;
    internal const long PartIndex = 9;
    internal const long CompleteIndex = 10;
    internal static readonly Guid BeginId = Guid.Parse("28175434-9a57-434e-b201-b376c9206f2d");

    private static readonly (long Index, Guid Id, OperationKind Kind, CommandOutcomeScopeKind Scope)[] Specifications = new[]
        {
            (CollectionIndex, Guid.Parse("bde5e9ef-a97c-46b2-baf8-1c09f4ce0c30"), OperationKind.ConfigureResource, CommandOutcomeScopeKind.Global),
            (TopicIndex, Guid.Parse("bf5a0b3b-23cb-40b6-90f0-44de34ae8d37"), OperationKind.ConfigureResource, CommandOutcomeScopeKind.Global),
            (QueueIndex, Guid.Parse("c5db5247-e280-4197-bb22-c428ece23790"), OperationKind.ConfigureResource, CommandOutcomeScopeKind.Global),
            (BatchIndex, ControlledPartitionMovementCorpus.SeedId, OperationKind.Batch, CommandOutcomeScopeKind.Partition),
            (BlobResourceIndex, Guid.Parse("a3aa5b55-c605-4c3b-9e49-871b876ef608"), OperationKind.ConfigureResource, CommandOutcomeScopeKind.Global),
            (BeginIndex, BeginId, OperationKind.BeginBlobUpload, CommandOutcomeScopeKind.Partition),
            (PartIndex, Guid.Parse("5738caef-a9e4-4623-ad93-d91e3838aff6"), OperationKind.WriteBlobPart, CommandOutcomeScopeKind.Partition),
            (CompleteIndex, Guid.Parse("7eeb58f9-989c-4a0a-9786-038ab9a8a1a7"), OperationKind.CompleteBlobUpload, CommandOutcomeScopeKind.Partition)
        };

    internal static async Task<ControlledPartitionMovementRetainedOutcome[]> CaptureAsync(ControlledPartitionMovementNode source)
    {
        var retained = new List<ControlledPartitionMovementRetainedOutcome>();
        foreach (var specification in Specifications)
        {
            var entry = source.Journal.Log.ReadEntry(specification.Index) ?? throw new InvalidOperationException(Missing);
            var operation = entry.Operation ?? throw new InvalidOperationException(Missing);
            await Assert.That(entry.Index).IsEqualTo(specification.Index);
            await Assert.That(operation.Id).IsEqualTo(specification.Id);
            await Assert.That(operation.Kind).IsEqualTo(specification.Kind);
            await Assert.That(operation.PrincipalId).IsEqualTo(PhysicalShardCatalogFixture.RootPrincipalId);
            var partition = ControlledPartitionMovementCorpus.Partition;
            var key = specification.Scope == CommandOutcomeScopeKind.Global
                ? KeySpace.GlobalOutcome(operation.PrincipalId, operation.Id)
                : KeySpace.PartitionOutcome(partition, operation.PrincipalId, operation.Id);
            var locator = specification.Scope == CommandOutcomeScopeKind.Partition
                ? KeySpace.OutcomeLocatorV2(partition, operation.PrincipalId, operation.Id) : null;
            var bytes = source.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(Missing);
            retained.Add(new(specification.Index, operation, specification.Scope, key, locator, bytes,
                NativeSerialization.Deserialize<StoredOutcome>(bytes)));
        }
        return retained.ToArray();
    }
}
