namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdMutations
{
    private const string AppendSamplesKind = "appendSamples";

    internal static CommandRequest Command(FunctionalGateContext context, int index, string blob)
    {
        var reference = context.Bind(new(context.Partition, FunctionalGateColdProtocol.Resource("docs", index), FunctionalGateColdProtocol.Id));
        var mutations = new List<Mutation>();
        if (index == 0)
        {
            mutations.Add(new PutDocument(context.Anchor.Collection, context.Anchor.Id, "{\"purpose\":\"cold-anchor\"}", 0));
        }
        mutations.AddRange([
            new PutDocument(reference.Collection, reference.Id, FunctionalGateColdProtocol.DocumentJson(context.Anchor, blob), 0),
            new PutDocument(FunctionalGateColdProtocol.Resource("vectors", index), reference.Id, FunctionalGateColdProtocol.Vector, 0),
            new PutVector(FunctionalGateColdProtocol.Resource("vectors", index), reference.Id, "/embedding", [1, 0], FunctionalGateSearch.Space, 1),
            new PutDocument(FunctionalGateColdProtocol.Resource("text", index), reference.Id, FunctionalGateColdProtocol.Text, 0),
            new AppendEvents(FunctionalGateColdProtocol.Resource("events", index), reference.Id,
                [FunctionalGateColdProtocol.Data(reference)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(FunctionalGateColdProtocol.Resource("queue", index), reference.Id,
                FunctionalGateColdProtocol.Data(reference).PayloadJson),
            new PublishTopic(FunctionalGateColdProtocol.Resource("topic", index), [FunctionalGateColdProtocol.Data(reference)]),
            new UpsertEdge(FunctionalGateColdProtocol.Resource("graph", index), FunctionalGateColdProtocol.Edge,
                reference, context.Anchor, "retained", "{}", 0),
            new AppendSamples(FunctionalGateColdProtocol.Resource("series", index), FunctionalGateColdProtocol.Series,
                [.. FunctionalGateColdProtocol.Samples], FunctionalGateColdProtocol.Tags(context.Anchor))
        ]);
        return new(Guid.NewGuid(), context.Partition, [.. mutations]);
    }

    internal static async Task ReceiptAsync(FunctionalGateContext context, int index, CommandRequest command, CommitReceipt receipt)
    {
        var expected = new List<MutationReceipt>();
        if (index == 0)
        {
            expected.Add(new("putDocument", context.Anchor.Collection, context.Anchor.Id, 1));
        }
        expected.AddRange([
            new("putDocument", FunctionalGateColdProtocol.Resource("docs", index), FunctionalGateColdProtocol.Id, 1),
            new("putDocument", FunctionalGateColdProtocol.Resource("vectors", index), FunctionalGateColdProtocol.Id, 1),
            new("putVector", FunctionalGateColdProtocol.Resource("vectors", index), FunctionalGateColdProtocol.Id, 1),
            new("putDocument", FunctionalGateColdProtocol.Resource("text", index), FunctionalGateColdProtocol.Id, 1),
            new("appendEvents", FunctionalGateColdProtocol.Resource("events", index), FunctionalGateColdProtocol.Id, 1),
            new("enqueue", FunctionalGateColdProtocol.Resource("queue", index), FunctionalGateColdProtocol.Id, 1),
            new("publishTopic", FunctionalGateColdProtocol.Resource("topic", index), FunctionalGateColdProtocol.Id, 1),
            new("upsertEdge", FunctionalGateColdProtocol.Resource("graph", index), FunctionalGateColdProtocol.Edge, 1),
            new(AppendSamplesKind, FunctionalGateColdProtocol.Resource("series", index), FunctionalGateColdProtocol.Series, 2)
        ]);
        await FunctionalGateRelationalAssertions.ReceiptAsync(context, receipt, command.CommandId, context.Partition, [.. expected]);
    }
}
