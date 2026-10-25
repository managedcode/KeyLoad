using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class ResourceIncarnationAssertions
{
    internal static ResourceLifetime Lifetime(ZoneTreeStore store, PartitionRef partition, string name = ResourceIncarnationLiteral.Table)
        => OrleansSerialization.Deserialize<ResourceLifetime>(Bytes(store, ResourceIncarnationLiteral.LifetimeKey(partition, name)));

    internal static byte[] Bytes(ZoneTreeStore store, byte[] key)
        => store.Read(view => view.ReadOwnedValue(key) ?? throw new InvalidOperationException("The original execution record is absent."));

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(expected),
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);

    internal static async Task ResultAsync(Result<object> expected, Result<object> actual)
    {
        await EqualAsync(Errors.Code(expected.Problem).Value, Errors.Code(actual.Problem).Value);
        await EqualAsync(expected.Problem?.Detail, actual.Problem?.Detail);
        await Assert.That(actual.Value?.GetType()).IsEqualTo(expected.Value?.GetType());
        await EqualAsync(expected.Value, actual.Value);
    }

    internal static async Task UnchangedAsync(ZoneTreeStore store, string[] image, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(image,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task IdentityAsync(ZoneTreeStore store, PartitionRef partition, Command<DatabaseCommand> original,
        ResourceDefinition definition, bool sql)
    {
        var fingerprint = JsonData.Fingerprint(new
        {
            Id = original.CommandId,
            Kind = original.Value!.Kind,
            PrincipalId = original.UserId!,
            PayloadJson = ResourceIncarnationLiteral.CreationJson(original)
        });
        await Assert.That(original.Value.Fingerprint).IsEqualTo(fingerprint);
        var incarnation = Convert.ToHexStringLower(SHA256.HashData(KeyCodec.Encode(
            "keyload:resource-incarnation", partition.TenantId, partition.DatabaseId, definition.Name, fingerprint)));
        if (!sql)
        {
            await Assert.That(fingerprint).IsEqualTo(ResourceIncarnationLiteral.Fingerprint);
            await Assert.That(incarnation).IsEqualTo(ResourceIncarnationLiteral.Incarnation);
        }
        var expected = new ResourceLifetime(partition.TenantId, partition.DatabaseId, definition.Name,
            definition.TransactionDomainId, definition.Kind, new(incarnation), fingerprint);
        await EqualAsync(expected, Lifetime(store, partition));
    }

    internal static async Task PreserveAsync(ZoneTreeStore store, PartitionRef partition, ResourceIncarnationState state)
    {
        await Assert.That(Bytes(store, ResourceIncarnationLiteral.LifetimeKey(partition))).IsEquivalentTo(state.LifetimeBytes,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(Bytes(store, KeySpace.GlobalOutcome(state.Original.UserId!, state.Original.CommandId)))
            .IsEquivalentTo(state.CreationOutcomeBytes, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await EqualAsync(state.Definition, store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, ResourceIncarnationLiteral.Table)))!);
    }

    internal static async Task<CommitReceipt> WriteAsync(DatabaseEngine database, PartitionRef partition, ResourceLifetime lifetime, long revision)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(ResourceIncarnationLiteral.Table, ResourceIncarnationLiteral.RowId,
                ResourceIncarnationLiteral.Row(revision), revision - 1)]);
        var operation = database.CreateCommand(OperationKind.Batch, command.CommandId, "root", database.EvaluationClock.GetUtcNow(), System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options));
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await EqualAsync<System.Collections.Immutable.ImmutableArray<MutationReceipt>>(
            [new("putDocument", ResourceIncarnationLiteral.Table, ResourceIncarnationLiteral.RowId, revision)], receipt.Mutations);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(1);
        await RowAsync(database, partition, lifetime, revision);
        return receipt;
    }

    internal static async Task RowAsync(DatabaseEngine database, PartitionRef partition, ResourceLifetime lifetime, long revision)
    {
        var reference = new EntityRef(partition, ResourceIncarnationLiteral.Table, ResourceIncarnationLiteral.RowId) { Lifetime = new(lifetime.Incarnation.Value) };
        await EqualAsync(new DocumentResult(reference, revision, ResourceIncarnationLiteral.Row(revision), false, []),
            database.GetDocument("root", reference)!);
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, ResourceIncarnationState state)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        foreach (var (command, result) in state.Outcomes)
        {
            await ResultAsync(result, database.Apply(command));
        }
        await UnchangedAsync(store, image, position);
    }
}
