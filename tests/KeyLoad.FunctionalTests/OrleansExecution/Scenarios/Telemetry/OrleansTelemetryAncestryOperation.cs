using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class OrleansTelemetryAncestryOperation
{
    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture, PrincipalRecord principal,
        Guid creation, string documentId, List<string> documents)
    {
        var commandId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var command = new CommandRequest(commandId, fixture.Database.Partition,
            [new PutDocument(OrleansTelemetryAncestryProtocol.Collection, documentId,
                OrleansTelemetryAncestryProtocol.Json)]);
        var placement = fixture.Database.Database.ReadAtomicPartitionPlacement(OrleansTelemetryAncestryProtocol.Root,
            fixture.Database.Partition);
        var reply = await RequestCqrsCommandStream.InvokeAsync(fixture, principal, requestId, commandId, command);
        await Assert.That(reply.Error).IsNull();
        var receipt = OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as CommitReceipt
            ?? throw new InvalidOperationException(OrleansTelemetryAncestryProtocol.MissingReceipt);
        var entry = OutcomeStoreOracle.ReadStored(fixture.Database.Database, commandId,
            TestContext.Current!.Execution.CancellationToken);
        var expected = new CommitReceipt(commandId, new(placement.Incarnation, fixture.Database.Partition.AtomicPartitionId, entry.CanonicalSequence), [new(OrleansTelemetryAncestryProtocol.PutKind, OrleansTelemetryAncestryProtocol.Collection,
                documentId, OrleansTelemetryAncestryProtocol.FirstRevision)], fixture.Database.Store.Identity.Durability, (fixture.Database.Store.Identity.Durability == DurabilityProfile.Buffered ? 0 : 1));
        await EqualAsync(expected, receipt);
        documents.Add(documentId);
        await CurrentAsync(fixture, principal, creation, documents, commandId, expected);
        var cut = fixture.Database.Store.Position;
        var journal = fixture.Database.ReadCanonicalCut();
        var replay = await RequestCqrsCommandStream.InvokeAsync(fixture, principal,
            Guid.NewGuid(), commandId, command);
        await Assert.That(replay.Error).IsNull();
        var replayReceipt = OrleansSerialization.Deserialize<GrainValue>(replay.Payload.Span).Value as CommitReceipt
            ?? throw new InvalidOperationException(OrleansTelemetryAncestryProtocol.MissingReceipt);
        await EqualAsync(expected, replayReceipt);
        await CurrentAsync(fixture, principal, creation, documents, commandId, expected);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
        await Assert.That(fixture.Database.ReadCanonicalCut()).IsEqualTo(journal);
    }

    private static async Task CurrentAsync(RequestCqrsHostedRuntimeFixture fixture, PrincipalRecord principal,
        Guid creation, List<string> documents, Guid commandId, CommitReceipt expected)
    {
        foreach (var id in documents)
        {
            var reference = fixture.Database.Reference(creation, OrleansTelemetryAncestryProtocol.Collection, id);
            var actual = fixture.Database.Database.GetDocument(OrleansTelemetryAncestryProtocol.Root, reference);
            await EqualAsync(new DocumentResult(reference, OrleansTelemetryAncestryProtocol.FirstRevision,
                OrleansTelemetryAncestryProtocol.Json, false, []), actual);
        }
        var retained = OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition,
            principal.Id, commandId) ?? throw new InvalidOperationException(OrleansTelemetryAncestryProtocol.MissingReceipt);
        await EqualAsync(expected, retained.Get<CommitReceipt>());
    }

    private static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(expected).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(actual))).IsTrue();
}
