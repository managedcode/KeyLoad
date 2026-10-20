using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ModelHeadAuthorityOperations
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task HealthyAsync(ModelHeadAuthorityFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        foreach (var queue in new[] { false, true })
        {
            await GraphLabelUseOperations.ObserveAsync(store, async () => await EqualAsync(
                engine.Execute(ModelHeadAuthorityFixture.Reader, ModelHeadAuthorityRequests.Sql(fixture.Harness.Partition, queue),
                    database.EvaluationClock, token), new QueryPage([.. ModelHeadAuthorityResults.Rows(queue)], null,
                        store.Position, ModelHeadAuthorityResults.Path(queue))));
            await GraphLabelUseOperations.ObserveAsync(store, async () => await EqualAsync(
                engine.ExecuteAst(ModelHeadAuthorityFixture.Reader, ModelHeadAuthorityRequests.Ast(fixture.Harness.Partition, queue),
                    database.EvaluationClock, token), new QueryPage([.. ModelHeadAuthorityResults.Rows(queue)], null,
                        store.Position, ModelHeadAuthorityResults.Path(queue))));
        }
        await TypedAsync(fixture, database, store, token);
    }
    private static async Task TypedAsync(ModelHeadAuthorityFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => await GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var source = fixture.Source;
            var expected = new AggregateReplayPage(source, new(2L, 1L, 1L), null,
                [new(source, 1L, 1L, new("created", "Created", ModelHeadAuthorityFixture.Payload("first"), ModelHeadAuthorityFixture.Headers), ModelHeadAuthorityFixture.Epoch),
                 new(source, 2L, 2L, new("paid", "Paid", ModelHeadAuthorityFixture.Payload("second"), ModelHeadAuthorityFixture.Headers), ModelHeadAuthorityFixture.Epoch)], store.Position);
            await EqualAsync(database.ReadAggregateReplay("root", new(source, "model-head", FromBeginning: true, MaximumEvents: 2), token), expected);
            await EqualAsync(database.InspectMessage("root", new(fixture.Harness.Partition, ModelHeadAuthorityFixture.Queue), "ready"),
                new MessageInspection(new("ready", MessageState.Ready, 0, 1L, 1L, ModelHeadAuthorityFixture.Epoch, null),
                    ModelHeadAuthorityFixture.Payload("queue"), ModelHeadAuthorityFixture.Headers));
        });
    internal static async Task RefuseAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        string subject, ErrorCode code, string detail, long generation, CancellationToken token)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await RefuseAsync(store, () => engine.Execute(subject, ModelHeadAuthorityRequests.Sql(partition, false, generation), database.EvaluationClock, token), code, detail);
        var ast = ModelHeadAuthorityRequests.Ast(partition, false, generation);
        await RefuseAsync(store, () => engine.ExecuteAst(subject, ast, database.EvaluationClock, token), code, detail);
        await RefuseAsync(store, () => engine.ExecuteAst(subject, ast with
        {
            Query = ast.Query with
            {
                Explain = true
            }
        },
            database.EvaluationClock, token), code, detail);
    }
    private static Task RefuseAsync(ZoneTreeStore store, Func<QueryPage> execute, ErrorCode code, string detail)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            QueryPage? partial = null;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = execute());
            await Assert.That(failure.Code).IsEqualTo(code);
            await Assert.That(failure.Message).IsEqualTo(detail);
            await Assert.That(partial).IsNull();
        });
    internal static Task ReplayAsync(ModelHeadAuthorityFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var actual = database.ApplyEmbedded(database.CreateCommand(OperationKind.Batch, fixture.Seed.CommandId, "root", default, System.Text.Json.JsonSerializer.Serialize(fixture.Seed, JsonDefaults.Options)), token).Get<CommitReceipt>();
            await EqualAsync(actual, fixture.Receipt);
            foreach (var effect in actual.Mutations)
            {
                await Assert.That(effect.CompositionReferences).IsEmpty();
            }
            var outcome = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(fixture.Harness.Partition, "root", fixture.Seed.CommandId)))!;
            await Assert.That(outcome.AsSpan().SequenceEqual(fixture.Outcome)).IsTrue();
        });
}
