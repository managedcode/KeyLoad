using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ModelRootPolicyAssertions
{
    internal static async Task WaveAsync(ModelRootPolicyFixture fixture, DatabaseEngine database, ZoneTreeStore store,
        bool use, bool read, CancellationToken token)
    {
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await ModelRootPolicyLifetime.ObserveAsync(store, async () =>
        {
            if (fixture.Queue)
            {
                var actual = database.InspectMessage(ModelRootPolicyFixture.Reader,
                    new(fixture.Harness.Partition, ModelRootPolicyFixture.Source), fixture.Id)!;
                await EqualAsync(actual with
                {
                    PayloadJson = Normalize(actual.PayloadJson!),
                    HeadersJson = Normalize(actual.HeadersJson!)
                },
                    ModelRootPolicyGolden.Message(fixture, read) with
                    {
                        PayloadJson = Normalize(ModelRootPolicyGolden.Payload(fixture, read)),
                        HeadersJson = Normalize(ModelRootPolicyGolden.Headers(fixture, read))
                    });
            }
            else
            {
                var actual = database.ReadStream(ModelRootPolicyFixture.Reader,
                    new ReadStreamRequest(new(fixture.Harness.Partition, ModelRootPolicyFixture.Source, ModelRootPolicyFixture.StreamId)),
                    cancellationToken: token);
                var expected = ModelRootPolicyGolden.Stream(fixture, database.LastApplied, read);
                await EqualAsync(actual with
                {
                    Events = [.. actual.Events.Select(Normalize)]
                },
                    expected with
                    {
                        Events = [.. expected.Events.Select(Normalize)]
                    });
            }
        });
        await PageAsync(store, () => queries.Execute(ModelRootPolicyFixture.Reader, ModelRootPolicyRequests.Sql(fixture), cancellationToken: token),
            ModelRootPolicyGolden.Page(fixture, store.Position, read));
        await PageAsync(store, () => queries.ExecuteAst(ModelRootPolicyFixture.Reader, ModelRootPolicyRequests.Ast(fixture), cancellationToken: token),
            ModelRootPolicyGolden.Page(fixture, store.Position, read));
        await PageAsync(store, () => queries.Execute(ModelRootPolicyFixture.Reader, ModelRootPolicyRequests.Sql(fixture, explicitValue: true), cancellationToken: token),
            ModelRootPolicyGolden.Page(fixture, store.Position, read, ModelRootPolicyGolden.Value(fixture, read), selectedFields: ModelRootPolicyGolden.ProtectedOmitted(fixture, read)));
        await PageAsync(store, () => queries.ExecuteAst(ModelRootPolicyFixture.Reader, ModelRootPolicyRequests.Ast(fixture, explicitValue: true), cancellationToken: token),
            ModelRootPolicyGolden.Page(fixture, store.Position, read, ModelRootPolicyGolden.Value(fixture, read), selectedFields: ModelRootPolicyGolden.ProtectedOmitted(fixture, read)));
        var value = System.Text.Json.JsonSerializer.Serialize(new
        {
            value = fixture.HeaderRoot ? "payload-public" : "header-public"
        }, JsonDefaults.Options);
        await PageAsync(store, () => queries.ExecuteAst(ModelRootPolicyFixture.Reader, ModelRootPolicyRequests.LiteralPublic(fixture), cancellationToken: token),
            ModelRootPolicyGolden.Page(fixture, store.Position, read, value, selectedFields: []));
        foreach (var order in new[] { false, true })
        {
            await UsageAsync(store, () => queries.Execute(ModelRootPolicyFixture.Reader,
                ModelRootPolicyRequests.Sql(fixture, filter: !order, order: order), cancellationToken: token), use,
                ModelRootPolicyGolden.Page(fixture, store.Position, read));
            await UsageAsync(store, () => queries.ExecuteAst(ModelRootPolicyFixture.Reader,
                ModelRootPolicyRequests.Ast(fixture, filter: !order, order: order), cancellationToken: token), use,
                ModelRootPolicyGolden.Page(fixture, store.Position, read));
        }
        await UsageAsync(store, () => queries.ExecuteAst(ModelRootPolicyFixture.Reader,
            ModelRootPolicyRequests.Join(fixture), cancellationToken: token), use,
            ModelRootPolicyGolden.Page(fixture, store.Position, read, ModelRootPolicyGolden.Value(fixture, read, true), true, ModelRootPolicyGolden.ProtectedOmitted(fixture, read)));
    }

    private static Task UsageAsync(ZoneTreeStore store, Func<QueryPage> operation, bool use, QueryPage expected)
        => use ? PageAsync(store, operation, expected) : ModelRootPolicyLifetime.ObserveAsync(store, async () =>
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => operation());
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(failure.Message).IsEqualTo("The query requires a protected field-use grant.");
        });

    private static Task PageAsync(ZoneTreeStore store, Func<QueryPage> operation, QueryPage expected)
        => ModelRootPolicyLifetime.ObserveAsync(store, async () =>
        {
            var actual = operation();
            await EqualAsync(actual with
            {
                Rows = [.. actual.Rows.Select(row => row with { Json = Normalize(row.Json) })]
            },
                expected with
                {
                    Rows = [.. expected.Rows.Select(row => row with { Json = Normalize(row.Json) })]
                });
        });
    private static EventRecord Normalize(EventRecord record) => record with
    {
        Data = record.Data with
        {
            PayloadJson = Normalize(record.Data.PayloadJson),
            HeadersJson = Normalize(record.Data.HeadersJson)
        }
    };
    private static string Normalize(string json) => JsonData.Validate(json, UnitExecutionOptions.DatabaseLimits().Value);
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
