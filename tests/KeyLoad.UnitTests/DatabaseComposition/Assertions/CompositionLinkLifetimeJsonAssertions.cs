using System.Text.Json;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Authorization;
using P = KeyLoad.UnitTests.DatabaseComposition.CompositionLinkLifetimeJsonProtocol;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal static class CompositionLinkLifetimeJsonAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));

    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.Length).IsEqualTo(expected.Mutations.Length);
        for (var index = 0; index < expected.Mutations.Length; index++)
        {
            await Assert.That(actual.Mutations[index].CompositionReferences.SequenceEqual(
                expected.Mutations[index].CompositionReferences)).IsTrue();
        }
    }

    internal static async Task ModelsAsync(SafeProjectorOperationDatabase owner, CompositionLinkLifetimeJsonState state,
        string? badPayload, bool healthy, CancellationToken token)
    {
        foreach (var (reference, json) in new[] { (state.From, P.FirstJson), (state.To, P.SecondJson) })
        {
            await EqualAsync(new DocumentResult(reference, 1, json, false, []),
                owner.Database.GetDocument(P.Root, reference, cancellationToken: token));
            await EqualAsync(new DocumentRecord(reference, 1, json, new(), new DateTimeOffset(state.Seed.Timestamp)),
                owner.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference))));
        }
        var edge = new EdgeRecord(P.Edge, state.From, state.To, P.Label, P.EmptyJson, 1);
        await EqualAsync(healthy ? edge : null, owner.Store.Read(view => view.GetRecord<EdgeRecord>(
            KeySpace.Partition("edge", owner.Source.Partition, P.Graph, P.Edge))));
        await EqualAsync(new global::KeyLoad.GraphTraversal(healthy ? [state.From, state.To] : [state.From], healthy ? [edge] : []),
            owner.Database.Traverse(P.Root, owner.Source.Partition, P.Graph, state.From, cancellationToken: token));
        await MessageAsync(owner, P.Queue, badPayload);
        await MessageAsync(owner, P.HealthyQueue, healthy ? P.Payload(state.From, state.To) : null);
    }

    internal static Task MessageAsync(SafeProjectorOperationDatabase owner, string queue, string? payload)
        => EqualAsync(payload is null ? null : new MessageInspection(new(P.Message, MessageState.Ready, 0, 1, 1, null, null),
            payload, P.EmptyJson), owner.Database.InspectMessage(P.Root, new(owner.Source.Partition, queue), P.Message));

    internal static async Task UnchangedAsync(SafeProjectorOperationDatabase owner, Func<Task> body)
    {
        var image = TeamRowPolicyAssertions.Image(owner.Store);
        var identity = OrleansSerialization.Serialize(owner.Store.Identity);
        var position = owner.Store.Position;
        var applied = owner.Database.LastApplied;
        var journal = owner.ReadCanonicalCut();
        await body();
        await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(owner.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(owner.Store.Position).IsEqualTo(position);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(owner.ReadCanonicalCut()).IsEqualTo(journal);
    }
}
