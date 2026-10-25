using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class CatalogArrayPathAssertions
{
    internal static async Task CheckAsync(TestDatabase creation, DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        bool use, bool read, long revision, CancellationToken token)
    {
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var expectedJson = revision == 1L ? read ? CatalogArrayPathFlow.Original : CatalogArrayPathFlow.Hidden
            : (read ? CatalogArrayPathFlow.Original : CatalogArrayPathFlow.Hidden).Replace("\"open\"", "\"closed\"", StringComparison.Ordinal);
        var omitted = read ? CatalogArrayPathFlow.Omitted.RemoveAt(0) : CatalogArrayPathFlow.Omitted;
        expectedJson = Normalize(expectedJson);
        QueryRow[] expected = [new(CatalogArrayPathFlow.Row, revision, expectedJson, true, omitted)];
        await CatalogArrayPathLifetime.ObserveAsync(store, async () =>
        {
            var reference = new EntityRef(partition, CatalogArrayPathFlow.Collection, CatalogArrayPathFlow.Row);
            var actual = database.GetDocument(CatalogArrayPathFlow.Reader, reference, cancellationToken: token);
            var document = new DocumentResult(creation.ExpectedReference(reference), revision, expectedJson, true, omitted);
            if (actual is null)
            {
                throw new InvalidOperationException("The catalog document must remain readable.");
            }
            await EqualAsync(actual with
            {
                Json = Normalize(actual.Json)
            }, document);
        });
        await ReadAsync(queries, store, partition, null, expected, token);
        var predicate = new Comparison(new FieldOperand(CatalogArrayPathFlow.SecretPath), "=", ValueOperand.Create("CANARY"));
        if (use)
        {
            await ReadAsync(queries, store, partition, predicate, expected, token);
        }
        else
        {
            await CatalogArrayPathLifetime.ObserveAsync(store, async () =>
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => queries.ExecuteAst(CatalogArrayPathFlow.Reader,
                    Request(partition, predicate), cancellationToken: token));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(failure.Message).IsEqualTo("The query requires a protected field-use grant.");
            });
        }
        for (var index = 0; index < CatalogArrayPathFlow.InvalidIndices.Length; index++)
        {
            var spelling = CatalogArrayPathFlow.InvalidIndices[index];
            await ReadAsync(queries, store, partition, new Comparison(new FieldOperand("/people/" + spelling + "/secret~0~1"),
                "=", ValueOperand.Create("CANARY")), [], token);
            await ReadAsync(queries, store, partition, new Comparison(new FieldOperand("/numbers/" + spelling),
                "=", ValueOperand.Create(CatalogArrayPathFlow.ObjectValues[index])), expected, token);
        }
        await ReadAsync(queries, store, partition, new Comparison(new FieldOperand("/people/1/secret~0~1"),
            "=", ValueOperand.Create("SECOND")), expected, token);
        await ReadAsync(queries, store, partition, new Comparison(new FieldOperand("/people/2/secret~0~1"),
            "=", ValueOperand.Create("CANARY")), [], token);
    }

    private static AstQueryRequest Request(PartitionRef partition, Predicate? predicate)
        => new(partition, new SelectQuery(CatalogArrayPathFlow.Collection, null, [new("*", "*")], predicate,
            [new("/@id", false)], 10), AllowFullScan: true);

    private static Task ReadAsync(QueryEngine queries, ZoneTreeStore store, PartitionRef partition,
        Predicate? predicate, QueryRow[] expected, CancellationToken token)
        => CatalogArrayPathLifetime.ObserveAsync(store, async () =>
        {
            var page = queries.ExecuteAst(CatalogArrayPathFlow.Reader, Request(partition, predicate), cancellationToken: token);
            await EqualAsync(page.Rows.Select(row => row with { Json = Normalize(row.Json) }).ToArray(), expected);
            await Assert.That(page.CutPosition).IsEqualTo(store.Position);
            await Assert.That(page.Cursor).IsNull();
            await Assert.That(page.AccessPath).IsEqualTo("bounded-full-scan");
        });

    internal static async Task ReceiptAsync(ZoneTreeStore store, CommandRequest command, CommitReceipt actual,
        long before, CommitToken expectedToken, long revision, string kind)
    {
        var expected = new CommitReceipt(command.CommandId, expectedToken, [new(kind, CatalogArrayPathFlow.Collection, CatalogArrayPathFlow.Row, revision)], DurabilityProfile.ProcessDurable, 1);
        await EqualAsync(actual, expected);
        await Assert.That(store.Position).IsEqualTo(before + 1L);
    }

    internal static byte[] Outcome(ZoneTreeStore store, CommandRequest command)
        => store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(command.Partition, CatalogArrayPathFlow.Root, command.CommandId)))
            ?? throw new InvalidOperationException("The acknowledged catalog operation outcome must exist.");

    internal static Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, CommandRequest command,
        CommitReceipt receipt, byte[] outcome, CancellationToken token)
        => CatalogArrayPathLifetime.ObserveAsync(store, async () =>
        {
            var operation = database.CreateCommand(OperationKind.Batch, command.CommandId, CatalogArrayPathFlow.Root, default, System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options));
            var replay = database.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>();
            await EqualAsync(replay, receipt);
            await Assert.That(Outcome(store, command).AsSpan().SequenceEqual(outcome)).IsTrue();
        });

    private static string Normalize(string json) => JsonData.Validate(json);

    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
