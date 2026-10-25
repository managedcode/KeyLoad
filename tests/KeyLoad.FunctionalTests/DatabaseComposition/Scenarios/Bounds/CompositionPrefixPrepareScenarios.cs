using System.Text.Json;
using D = KeyLoad.FunctionalTests.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal static class CompositionPrefixPrepareScenarios
{
    internal static string Link(CompositionPrefixPrepareOwner owner) => JsonSerializer.Serialize(new QueueGraphLink(
        CompositionPrefixPrepareSeed.Fresh(owner, D.From, D.First),
        CompositionPrefixPrepareSeed.Fresh(owner, D.To, D.Second), D.Label), JsonDefaults.Options);

    internal static async Task StagedPutAsync(CompositionPrefixPrepareOwner owner, bool typed = true)
    {
        await CompositionPrefixPrepareSeed.VerticesAsync(owner, first: false);
        var payload = Link(owner);
        await CompositionPrefixPrepareCommands.CommitAsync(owner,
            [new PutDocument(D.From, D.First, D.Json, D.Absent), new EnqueueMessage(D.Queue, D.Message, payload),
             CompositionPrefixPrepareCommands.Forward(typed: typed)],
            [new(D.Put, D.From, D.First, D.FirstRevision), new(D.Enqueue, D.Queue, D.Message, D.FirstRevision),
             CompositionPrefixPrepareCommands.Edge(owner, D.Graph, D.Forward + D.Message)]);
        await CompositionPrefixPrepareAssertions.ColdAsync(owner, async () =>
        {
            await CompositionPrefixPrepareAssertions.EdgeAsync(owner, D.Graph, D.Forward + D.Message);
            await CompositionPrefixPrepareAssertions.EqualAsync(new DocumentResult(
                CompositionPrefixPrepareSeed.Bound(owner, D.From, D.First), D.FirstRevision, D.Json, false, []),
                owner.Database.GetDocument(D.Root, CompositionPrefixPrepareSeed.Fresh(owner, D.From, D.First)));
            await Assert.That(owner.Database.InspectMessage(D.Root, new(owner.Source.Partition, D.Queue), D.Message)!.PayloadJson)
                .IsEqualTo(payload);
        });
    }

    internal static async Task CancelledAsync(CompositionPrefixPrepareOwner owner)
    {
        await CompositionPrefixPrepareSeed.VerticesAsync(owner);
        var administrator = await CompositionPrefixPrepareSeed.QueueAdministratorAsync(owner);
        // Valid opaque JSON, intentionally not a QueueGraphLink. Cancellation must remove selection before parsing.
        await CompositionPrefixPrepareCommands.CommitAsync(owner,
            [new EnqueueMessage(D.Queue, D.Message, D.EmptyJson),
             new CancelQueueMessage(D.Queue, D.Message, D.FirstRevision, D.FirstRevision),
             CompositionPrefixPrepareCommands.Forward()],
            [new(D.Enqueue, D.Queue, D.Message, D.FirstRevision), new(D.Cancel, D.Queue, D.Message, D.CancelRevision)],
            principal: administrator);
        await CompositionPrefixPrepareAssertions.ColdAsync(owner, async () =>
        {
            await Assert.That(owner.Database.Traverse(D.Root, owner.Source.Partition, D.Graph,
                CompositionPrefixPrepareSeed.Fresh(owner, D.From, D.First)).Edges).IsEmpty();
            var message = owner.Database.InspectMessage(D.Root, new(owner.Source.Partition, D.Queue), D.Message)!;
            await Assert.That(message.Metadata.State).IsEqualTo(MessageState.Cancelled);
            await Assert.That(message.PayloadJson).IsNull();
            await Assert.That(message.Metadata.StateVersion).IsEqualTo(D.CancelRevision);
        });
    }

    internal static async Task ReverseAsync(CompositionPrefixPrepareOwner owner)
    {
        await CompositionPrefixPrepareSeed.VerticesAsync(owner);
        var from = CompositionPrefixPrepareSeed.Fresh(owner, D.From, D.First);
        var to = CompositionPrefixPrepareSeed.Fresh(owner, D.To, D.Second);
        await CompositionPrefixPrepareCommands.CommitAsync(owner,
            [new UpsertEdge(D.Graph, D.Edge, from, to, D.Label, ExpectedRevision: D.Absent),
             new GraphToQueueMutation(D.Queue, D.Graph, from, D.Reverse),
             CompositionPrefixPrepareCommands.Forward(D.RoundGraph, D.Round)],
            [new(D.Upsert, D.Graph, D.Edge, D.FirstRevision),
             new(D.Enqueue, D.Queue, D.Reverse + D.Edge, D.FirstRevision)
             { CompositionReferences = [CompositionPrefixPrepareSeed.Bound(owner, D.From, D.First),
                 CompositionPrefixPrepareSeed.Bound(owner, D.To, D.Second)] },
             CompositionPrefixPrepareCommands.Edge(owner, D.RoundGraph, D.Round + D.Reverse + D.Edge)]);
        await CompositionPrefixPrepareAssertions.ColdAsync(owner, async () =>
        {
            await CompositionPrefixPrepareAssertions.EdgeAsync(owner, D.Graph, D.Edge);
            await CompositionPrefixPrepareAssertions.EdgeAsync(owner, D.RoundGraph, D.Round + D.Reverse + D.Edge);
            var message = owner.Database.InspectMessage(D.Root, new(owner.Source.Partition, D.Queue), D.Reverse + D.Edge)!;
            await CompositionPrefixPrepareAssertions.EqualAsync(new QueueGraphLink(
                CompositionPrefixPrepareSeed.Bound(owner, D.From, D.First),
                CompositionPrefixPrepareSeed.Bound(owner, D.To, D.Second), D.Label),
                JsonSerializer.Deserialize<QueueGraphLink>(message.PayloadJson!, JsonDefaults.Options));
        });
    }
}
