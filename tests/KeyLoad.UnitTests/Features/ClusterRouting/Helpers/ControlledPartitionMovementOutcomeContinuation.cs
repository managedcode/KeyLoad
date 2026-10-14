using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementOutcomeContinuation
{
    private const string ChangedContent = "The command ID was already used with different content.";
    private const string ChangedDomain = "changed-movement-domain";
    private const long EntryStep = 1;
    private static readonly Guid HealthyId = Guid.Parse("f0287e50-3e06-4cda-850f-8b236008eb18");

    internal static async Task ConflictAsync(ControlledPartitionMovementNode source, ReplicatedOperation original)
    {
        var request = NativeCommandPayload.Read<ConfigureResourceRequest>(original);
        var changed = request with
        {
            Definition = request.Definition with
            {
                TransactionDomainId = ChangedDomain
            }
        };
        var operation = source.Database.CreateNativeOperation(original.Kind, original.Id, original.PrincipalId,
            original.EvaluatedAt, NativeSerialization.Serialize(changed));
        var denied = source.Database.ResolveOutcome(operation);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(denied.NativeValue).IsNull();
        await Assert.That(denied.Json).IsNull();
        await Assert.That(denied.SafeDetail).IsEqualTo(ChangedContent);
    }

    internal static async Task HealthyColdAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, CancellationToken cancellationToken)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var definition = ControlledPartitionMovementOutcomeValueAssertions.Resource(ControlledPartitionMovementOutcomeInventory.CollectionIndex);
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        var original = source.Database.CreateNativeOperation(OperationKind.ConfigureResource, HealthyId,
            PhysicalShardCatalogFixture.RootPrincipalId, source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var before = source.Journal.Log.State.LastIndex;
        var beforePosition = source.Store.Position;
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var targetPosition = target.Store.Position;
        var targetIndex = target.Journal.Log.State.LastIndex;
        var result = source.Journal.Submit(original, cancellationToken);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        await ControlledPartitionMovementOutcomeValueAssertions.EqualAsync(result.Get<ResourceDefinition>(), definition);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(checked(before + EntryStep));
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        await Assert.That(source.Store.Position).IsEqualTo(checked(beforePosition + EntryStep));
        var sourcePosition = source.Store.Position;
        source.Reopen();
        target.Reopen();
        await Assert.That(NativeSerialization.Serialize(source.Journal.Submit(original, cancellationToken))
            .SequenceEqual(NativeSerialization.Serialize(result))).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(checked(before + EntryStep));
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(targetIndex);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage, StringComparer.Ordinal)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage, StringComparer.Ordinal)).IsTrue();
    }
}
