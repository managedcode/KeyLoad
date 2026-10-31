using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using static KeyLoad.FunctionalTests.ResourceExecution.ResourcePairBindingState;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class ResourcePairBindingFlow
{
    private const byte MalformedTag = 1;
    private const byte MalformedLength = 2;
    private const byte MalformedTail = 3;

    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Configure(state);
        await ResourcePairBindingBatch.RunAsync(state, token);
        await ResourcePairBindingSql.RunAsync(state, token);
        await ResourcePairBindingQuery.RunAsync(state, token);
        await ResourcePairBindingBm25.RunAsync(state, token);
        await ResourcePairBindingAssertions.PairsAsync(state, state.Source.Store);
        await ResourcePairBindingCold.RunAsync(state, token);
    }

    private static void Configure(ResourcePairBindingState state)
    {
        var source = state.Source;
        ResourceDefinition[] definitions =
        [
            new(Documents, ResourceKind.Collection, source.Partition.TransactionDomainId),
            new(Events, ResourceKind.StreamSet, source.Partition.TransactionDomainId),
            new(Queue, ResourceKind.WorkQueue, source.Partition.TransactionDomainId) { QueuePolicy = new() },
            new(Table, ResourceKind.Collection, source.Partition.TransactionDomainId)
            {
                Authority = DocumentAuthority.Document,
                RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new(ValueColumn, RelationalColumnType.WholeNumber)])
            }
        ];
        foreach (var definition in definitions)
        {
            _ = source.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(source.Partition.TenantId,
                source.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
            state.Pairs.Add(definition.Name, (ResourceIncarnationAssertions.Bytes(source.Store,
                KeySpace.Resource(source.Partition.TenantId, source.Partition.DatabaseId, definition.Name)),
                ResourceIncarnationAssertions.Bytes(source.Store, LifetimeKey(source.Partition, definition.Name))));
        }
    }

    internal static byte[] LifetimeKey(PartitionRef partition, string name)
        => KeyCodec.Encode(LifetimeSpace, partition.TenantId, partition.DatabaseId, name);

    internal static (byte[] Key, byte[] Original) Install(ResourcePairBindingState state, string name)
    {
        var partition = state.Source.Partition;
        var definition = KeySpace.Resource(partition.TenantId, partition.DatabaseId, name);
        var lifetime = LifetimeKey(partition, name);
        var key = state.Fault == ResourcePairBindingFault.MissingDefinition ? definition : lifetime;
        var original = ResourceIncarnationAssertions.Bytes(state.Source.Store, key);
        var changed = state.Fault switch
        {
            ResourcePairBindingFault.MissingLifetime or ResourcePairBindingFault.MissingDefinition => null,
            ResourcePairBindingFault.MalformedLifetime => [MalformedTag, MalformedLength, MalformedTail],
            ResourcePairBindingFault.ConflictingLifetime => Conflict(original),
            _ => throw new InvalidOperationException(Invalid)
        };
        state.Source.Store.Commit((transaction, _) =>
        {
            if (changed is null)
            {
                transaction.Delete(key);
            }
            else
            {
                transaction.Put(key, changed);
            }
            return true;
        });
        return (key, original);
    }

    internal static void Repair(ZoneTreeStore store, (byte[] Key, byte[] Original) row)
        => store.Commit((transaction, _) => { transaction.Put(row.Key, row.Original); return true; });

    private static byte[] Conflict(byte[] original)
    {
        var lifetime = OrleansSerialization.Deserialize<ResourceLifetime>(original);
        var value = lifetime.Incarnation.Value.ToCharArray();
        value[Zero] = value[Zero] == HexZero ? HexOne : HexZero;
        return OrleansSerialization.Serialize(lifetime with
        {
            Incarnation = lifetime.Incarnation with { Value = new string(value) }
        });
    }

    internal static async Task<(Command<DatabaseCommand> Original, Result<object> Result)> ApplyAfterRepairAsync(
        ResourcePairBindingState state, Command<DatabaseCommand> issued, string resource, CancellationToken token)
    {
        var source = state.Source;
        var damaged = Install(state, resource);
        var image = QueueWholeFlowStorage.Bytes(source.Store);
        var cut = source.ReadCanonicalCut();
        var failures = new List<Exception>();
        try
        {
            for (var attempt = Zero; attempt < TwoColdWaves; attempt += One)
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => source.SubmitIssuedEmbedded(issued, token));
                failures.Add(failure);
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
                await RefusedAsync(source, issued, image, cut);
            }
            Repair(source.Store, damaged);
            var result = source.SubmitIssuedEmbedded(issued, token);
            var stored = source.ReadStoredOutcome(issued);
            var original = stored.PreparedCommand;
            await Assert.That(stored.CanonicalSequence).IsEqualTo(cut.AppliedPosition + One);
            await Assert.That(source.Database.LastApplied).IsEqualTo(cut.AppliedPosition + One);
            await Assert.That(original.CommandId).IsEqualTo(issued.CommandId);
            await Assert.That(original.UserId).IsEqualTo(issued.UserId);
            await Assert.That(original.Value!.Kind).IsEqualTo(issued.Value!.Kind);
            await Assert.That(original.Value.Fingerprint).IsEqualTo(issued.Value.Fingerprint);
            await Assert.That(OrleansSerialization.Serialize(original.Value.Input, ValidationProfile.PublicInputElements).AsSpan().SequenceEqual(OrleansSerialization.Serialize(issued.Value.Input, ValidationProfile.PublicInputElements).AsSpan())).IsTrue();
            state.Remember(original, result);
            await ResourcePairBindingAssertions.PairsAsync(state, source.Store);
            return (original, result);
        }
        catch (Exception failure)
        {
            failure.Data[SubmissionFailuresKey] = failures.ToArray();
            throw;
        }
    }

    private static async Task RefusedAsync(TestDatabase source, Command<DatabaseCommand> original,
        string[] image, (long AppliedPosition, long StorePosition) cut)
    {
        var result = source.Database.ResolveOutcome(original);
        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(result.Value).IsNull();
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
            source.Partition, original.UserId!, original.CommandId)))).IsNull();
        await Assert.That(source.ReadCanonicalCut()).IsEqualTo(cut);
        await ResourceIncarnationAssertions.UnchangedAsync(source.Store, image, cut.StorePosition);
    }
}
