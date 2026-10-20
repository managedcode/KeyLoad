using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;
using A = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionBoundReferenceAssertions;
using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionBoundReferenceData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionBoundReferenceReads
{
    internal static EntityRef Reference(SafeProjectorOperationDatabase owner, string name, string id) => owner.Store.Read(view =>
    {
        var partition = owner.Source.Partition;
        var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId, partition.DatabaseId, name));
        var lifetime = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId, name, definition)
            ?? throw new InvalidOperationException("The configured composition endpoint has no actual lifetime.");
        return new EntityRef(partition, name, id) { Lifetime = new(lifetime.Incarnation.Value) };
    });

    internal static async Task VerifyAsync(SafeProjectorOperationDatabase owner, CompositionBoundReferenceState state,
        CancellationToken token)
    {
        foreach (var (reference, json) in new[] { (state.From, D.FirstJson), (state.To, D.SecondJson) })
        {
            await A.EqualAsync(new DocumentResult(reference, 1, json, false, []),
                owner.Database.GetDocument(D.Root, reference, cancellationToken: token));
            var raw = owner.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference)))!;
            await A.EqualAsync(new DocumentRecord(reference, 1, json, new(), new DateTimeOffset(state.Commands[0].Operation.Timestamp)), raw);
        }
        foreach (var (graph, id) in new[] { (D.Graph, D.EdgeId), (D.RoundGraph, D.RoundEdgeId) })
        {
            var expected = new EdgeRecord(id, state.From, state.To, D.Label, D.Attributes, 1);
            var raw = owner.Store.Read(view => view.GetRecord<EdgeRecord>(KeySpace.Partition("edge", owner.Source.Partition, graph, id)));
            await A.EqualAsync(expected, raw);
            await A.EqualAsync(new global::KeyLoad.GraphTraversal([state.From, state.To], [expected]),
                owner.Database.Traverse(D.Root, owner.Source.Partition, graph, state.From, cancellationToken: token));
        }
        await A.EqualAsync(new global::KeyLoad.GraphTraversal([state.From], []),
            owner.Database.Traverse(D.Root, owner.Source.Partition, D.EmptyGraph, state.From, cancellationToken: token));
        var plainFrom = state.From with
        {
            Lifetime = null
        };
        var plainTo = state.To with
        {
            Lifetime = null
        };
        await MessageAsync(owner, D.Queue, D.Message, D.Payload(plainFrom, plainTo));
        await MessageAsync(owner, D.ReverseQueue, D.DerivedId, D.Payload(state.From, state.To));
    }

    private static Task MessageAsync(SafeProjectorOperationDatabase owner, string queue, string id, string payload)
        => A.EqualAsync(new MessageInspection(new(id, MessageState.Ready, 0, 1, 1, null, null), payload, D.Headers),
            owner.Database.InspectMessage(D.Root, new(owner.Source.Partition, queue), id));
}
