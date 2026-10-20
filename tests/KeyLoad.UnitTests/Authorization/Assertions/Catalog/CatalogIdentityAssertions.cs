using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class CatalogIdentityAssertions
{
    internal static async Task HealthyAsync(TestDatabase creation, DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        bool use, bool read, long revision, CancellationToken token)
    {
        var json = revision == 2L ? CatalogIdentityProtocol.Current : read ? CatalogIdentityProtocol.Original : CatalogIdentityProtocol.Hidden;
        string[] omitted = read ? [] : ["/secret"];
        var expected = new QueryPage([new(CatalogIdentityProtocol.Row, revision, json, !read, [.. omitted])],
            null, store.Position, "bounded-full-scan");
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await CatalogIdentityLifetime.ObserveAsync(store, async () =>
        {
            var subject = database.Authenticate(CatalogIdentityProtocol.Secret, database.EvaluationClock.GetUtcNow());
            await Assert.That(subject).IsEqualTo(CatalogIdentityProtocol.Reader);
            var reference = new EntityRef(partition, CatalogIdentityProtocol.Collection, CatalogIdentityProtocol.Row);
            await EqualAsync(database.GetDocument(subject, reference, cancellationToken: token),
                new DocumentResult(creation.ExpectedReference(reference), revision, json, !read, [.. omitted]));
        });
        foreach (var ast in new[] { false, true })
        {
            await CatalogIdentityLifetime.ObserveAsync(store, async () =>
            {
                var subject = database.Authenticate(CatalogIdentityProtocol.Secret, database.EvaluationClock.GetUtcNow());
                var actual = ast ? queries.ExecuteAst(subject, CatalogIdentityProtocol.Request(partition), cancellationToken: token)
                    : queries.Execute(subject, CatalogIdentityProtocol.Sql(partition), cancellationToken: token);
                await EqualAsync(actual, expected);
            });
        }
        await CatalogIdentityLifetime.ObserveAsync(store, async () =>
        {
            var request = CatalogIdentityProtocol.Request(partition, true);
            if (use)
            {
                await EqualAsync(queries.ExecuteAst(CatalogIdentityProtocol.Reader, request, cancellationToken: token), expected);
            }
            else
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => queries.ExecuteAst(CatalogIdentityProtocol.Reader, request,
                    cancellationToken: token));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(failure.Message).IsEqualTo("The query requires a protected field-use grant.");
            }
        });
    }

    internal static async Task RefusedAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        ErrorCode code, string detail, bool directSubject, CancellationToken token)
    {
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        Action[] requests = [() => database.Authenticate(CatalogIdentityProtocol.Secret, database.EvaluationClock.GetUtcNow()),
            () => database.GetDocument(database.Authenticate(CatalogIdentityProtocol.Secret, database.EvaluationClock.GetUtcNow()),
                new(partition, CatalogIdentityProtocol.Collection, CatalogIdentityProtocol.Row), cancellationToken: token),
            () => queries.Execute(database.Authenticate(CatalogIdentityProtocol.Secret, database.EvaluationClock.GetUtcNow()),
                CatalogIdentityProtocol.Sql(partition), cancellationToken: token),
            () => queries.ExecuteAst(database.Authenticate(CatalogIdentityProtocol.Secret, database.EvaluationClock.GetUtcNow()),
                CatalogIdentityProtocol.Request(partition), cancellationToken: token)];
        foreach (var request in requests)
        {
            await ErrorAsync(store, request, code, detail);
        }
        if (directSubject)
        {
            await ErrorAsync(store, () => database.GetDocument(CatalogIdentityProtocol.Reader,
                new(partition, CatalogIdentityProtocol.Collection, CatalogIdentityProtocol.Row), cancellationToken: token), code, detail);
            await ErrorAsync(store, () => queries.Execute(CatalogIdentityProtocol.Reader,
                CatalogIdentityProtocol.Sql(partition), cancellationToken: token), code, detail);
            await ErrorAsync(store, () => queries.ExecuteAst(CatalogIdentityProtocol.Reader,
                CatalogIdentityProtocol.Request(partition), cancellationToken: token), code, detail);
        }
        await ErrorAsync(store, () => database.Authenticate(CatalogIdentityProtocol.WrongSecret, database.EvaluationClock.GetUtcNow()),
            ErrorCode.Unauthenticated, CatalogIdentityProtocol.Unavailable);
    }

    private static Task ErrorAsync(ZoneTreeStore store, Action request, ErrorCode code, string detail)
        => CatalogIdentityLifetime.ObserveAsync(store, async () =>
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(request);
            await Assert.That(failure.Code).IsEqualTo(code);
            await Assert.That(failure.Message).IsEqualTo(detail);
        });

    internal static async Task ReceiptAsync(ZoneTreeStore store, CommandRequest command, CommitReceipt actual,
        long before, CommitToken expectedToken, long revision, string kind)
    {
        var expected = new CommitReceipt(command.CommandId, expectedToken, [new(kind, CatalogIdentityProtocol.Collection, CatalogIdentityProtocol.Row, revision)], DurabilityProfile.ProcessDurable, 1);
        await EqualAsync(actual, expected);
        await Assert.That(store.Position).IsEqualTo(before + 1L);
    }

    internal static byte[] Outcome(ZoneTreeStore store, CommandRequest command, string principal)
        => store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(command.Partition, principal, command.CommandId)))
            ?? throw new InvalidOperationException("The complete acknowledged execution outcome must exist.");

    internal static Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, CommandRequest command,
        CommitReceipt receipt, byte[] outcome, string principal, CancellationToken token)
        => CatalogIdentityLifetime.ObserveAsync(store, async () =>
        {
            var operation = database.CreateCommand(OperationKind.Batch, command.CommandId, principal, default, System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options));
            await EqualAsync(database.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>(), receipt);
            await Assert.That(Outcome(store, command, principal).AsSpan().SequenceEqual(outcome)).IsTrue();
        });

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
