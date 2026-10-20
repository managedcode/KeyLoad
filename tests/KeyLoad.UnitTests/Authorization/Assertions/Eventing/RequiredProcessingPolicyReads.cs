using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class RequiredProcessingPolicyReads
{
    internal static Task LeasedAsync(RequiredProcessingPolicyFixture fixture, Delivery delivery, bool redacted, CancellationToken token)
        => RequiredProcessingPolicyAssertions.ObserveAsync(fixture.Harness.Store, async () =>
        {
            var database = fixture.Harness.Database;
            await RequiredProcessingPolicyAssertions.EqualAsync<DocumentResult?>(null, database.GetDocument("root", fixture.Reference("first"), cancellationToken: token));
            var metadata = new MessageMetadata("first", MessageState.Leased, 1, 2L, 1L, null, null,
                RequiredProcessingPolicyFixture.Worker, 1L, delivery.LeaseUntil, 1L);
            await RequiredProcessingPolicyAssertions.EqualAsync(new MessageInspection(metadata, fixture.Payload, fixture.Headers),
                database.InspectMessage("root", fixture.Lane, "first"));
            await RequiredProcessingPolicyAssertions.EqualAsync(new MessageInspection(metadata,
                redacted ? "{\"value\":1}" : fixture.Payload, redacted ? "{\"trace\":\"one\"}" : fixture.Headers),
                database.InspectMessage(RequiredProcessingPolicyFixture.Worker, fixture.Lane, "first"));
            if (redacted)
            {
                var query = new AstQueryRequest(fixture.Harness.Partition,
                    new(RequiredProcessingPolicyFixture.Queue, null, [new("*", "*")], null, [], 10,
                        ModelSource: new(ModelQuerySourceKind.QueueMessages, RequiredProcessingPolicyFixture.Queue)), AllowFullScan: true);
                var page = new QueryEngine(database, UnitExecutionOptions.QueryExecution()).ExecuteAst(RequiredProcessingPolicyFixture.Worker, query, cancellationToken: token);
                const string json = """{"id":"first","state":"Leased","attempts":1,"stateVersion":2,"notBefore":null,"expiresAt":null,"payload":{"value":1},"headers":{"trace":"one"}}""";
                await RequiredProcessingPolicyAssertions.EqualAsync(new QueryPage([new("first", 2L, json, true,
                    [fixture.Resource.FieldPolicies.IsEmpty ? "/headers/secret" : "/payload/secret"])], null,
                    fixture.Harness.Store.Position, "model-scan:queue-messages"), page);
            }
        });
    internal static Task FinalAsync(RequiredProcessingPolicyFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => RequiredProcessingPolicyAssertions.ObserveAsync(store, async () =>
        {
            foreach (var (id, sequence) in new[] { ("first", 1L), ("next", 2L) })
            {
                await RequiredProcessingPolicyAssertions.EqualAsync(new DocumentResult(fixture.Harness.ExpectedReference(fixture.Reference(id)), 1L,
                    RequiredProcessingPolicyFixture.EffectJson(id), false, []), database.GetDocument("root", fixture.Reference(id), cancellationToken: token));
                await RequiredProcessingPolicyAssertions.EqualAsync(new MessageInspection(new(id, MessageState.Acked, 1, 3L, sequence,
                    null, null, null, 1L, null, 1L), null, null), database.InspectMessage("root", fixture.Lane, id));
            }
            var status = database.GetOutboxStatus("root", fixture.Harness.Partition);
            await Assert.That(status.Head.Tail).IsEqualTo(4L);
            await Assert.That(status.Head.FirstAvailable).IsEqualTo(1L);
            await Assert.That(status.Head.StoredRecords).IsEqualTo(4L);
            await Assert.That(status.Head.StoredBytes).IsGreaterThan(0L);
            await Assert.That(status.Consumers).IsEmpty();
        });
}
