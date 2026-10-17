using KeyLoad.Query;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryLifetimeNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BothAuthoritiesRestrictRowsBeforeQueryPredicatesAndStatisticsThenRepairReplayAndTwoColdOwners(bool sourceNarrower)
        => await RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var flow = new QueryLifetimeNativeFlow(fixture, sourceNarrower);
            flow.Seed(restrictedRows: true);
            await QueryLifetimeNativeAssertions.UnchangedAsync(flow, async () =>
            {
                await Assert.That(flow.Query().Candidates.Length).IsEqualTo(QueryLifetimeNativeProtocol.NoRows);
                var expected = new KeyLoad.Query.Features.QueryExecution.DistributedTextStatisticsV1(
                    [QueryLifetimeNativeProtocol.Text], QueryLifetimeNativeProtocol.NoRows, QueryLifetimeNativeProtocol.NoRows,
                    [QueryLifetimeNativeProtocol.NoRows]);
                await Assert.That(JsonDefaults.Serialize(flow.Statistics().Witness.Statistics).AsSpan()
                    .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            flow.RepairRows();
            await QueryLifetimeNativeAssertions.ColdReplayAsync(flow);
        });

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BothAuthoritiesProjectThenHideKnownAndUnknownSchemaBeforeFieldBindingWithoutChangingState(bool sourceNarrower)
        => await RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var flow = new QueryLifetimeNativeFlow(fixture, sourceNarrower);
            flow.Seed(restrictedRows: false);
            await QueryLifetimeNativeAssertions.UnchangedAsync(flow, () => QueryLifetimeNativeAssertions.HealthyAsync(flow));
            await QueryLifetimeNativeAssertions.UnchangedAsync(flow, async () =>
            {
                var selected = flow.Query(RemoteDocumentNativeFixture.Secret).Candidates.Single().Row;
                var expected = new QueryRow(RemoteDocumentNativeFixture.DocumentId, RemoteDocumentNativeFixture.First,
                    QueryLifetimeNativeProtocol.NullSecretJson, true, [RemoteDocumentNativeFixture.Secret]);
                await Assert.That(JsonDefaults.Serialize(selected).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            await QueryLifetimeNativeAssertions.UnchangedAsync(flow, async () =>
            {
                var filter = new Comparison(new FieldOperand(RemoteDocumentNativeFixture.Secret), QueryLifetimeNativeProtocol.EqualsOperator,
                    new ValueOperand(System.Text.Json.JsonSerializer.SerializeToElement(QueryLifetimeNativeProtocol.PrivateValue)));
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Query(filter: filter),
                    ErrorCode.PermissionDenied, QueryLifetimeNativeProtocol.UseDenied);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Statistics(
                    field: RemoteDocumentNativeFixture.Secret, text: QueryLifetimeNativeProtocol.PrivateTerm),
                    ErrorCode.PermissionDenied, QueryLifetimeNativeProtocol.UseDenied);
                var unknown = flow.Query(QueryLifetimeNativeProtocol.MissingPath).Candidates.Single().Row;
                var expected = new QueryRow(RemoteDocumentNativeFixture.DocumentId, RemoteDocumentNativeFixture.First,
                    QueryLifetimeNativeProtocol.NullSecretJson, false, []);
                await Assert.That(JsonDefaults.Serialize(unknown).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            flow.HideNarrowSchema();
            await QueryLifetimeNativeAssertions.UnchangedAsync(flow, async () =>
            {
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Query(RemoteDocumentNativeFixture.Secret),
                    ErrorCode.Validation, QueryLifetimeNativeProtocol.SchemaDenied);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Query(QueryLifetimeNativeProtocol.MissingPath),
                    ErrorCode.Validation, QueryLifetimeNativeProtocol.SchemaDenied);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Statistics(),
                    ErrorCode.Validation, QueryLifetimeNativeProtocol.SchemaDenied);
                var hidden = flow.Query().Candidates.Single().Row;
                var expected = new QueryRow(RemoteDocumentNativeFixture.DocumentId, RemoteDocumentNativeFixture.First,
                    RemoteDocumentNativeFixture.ProjectedJson, true, []);
                await Assert.That(JsonDefaults.Serialize(hidden).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            flow.RepairSchema();
            await QueryLifetimeNativeAssertions.ColdReplayAsync(flow);
        });

    [Test]
    public async Task ActualIndependentLifetimeCannotSatisfyAnotherResourceLeafThenHealthyReplaySurvivesTwoColdOwners()
        => await RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var flow = new QueryLifetimeNativeFlow(fixture, sourceNarrower: true);
            flow.Seed(restrictedRows: false);
            var original = flow.Capture();
            var other = flow.CreateOtherLifetime();
            var wrong = original with
            {
                Coverage = other
            };
            await QueryLifetimeNativeAssertions.UnchangedAsync(flow, async () =>
            {
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Query(captured: wrong), ErrorCode.Corruption);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Statistics(wrong), ErrorCode.Corruption);
                var missing = original with
                {
                    Coverage = null!
                };
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Query(captured: missing), ErrorCode.Corruption);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Statistics(missing), ErrorCode.Corruption);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Query(captured: missing, nativeRoundTrip: false),
                    ErrorCode.OwnershipLost);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Statistics(missing, nativeRoundTrip: false),
                    ErrorCode.OwnershipLost);
                var malformed = original with
                {
                    Coverage = original.Coverage with
                    {
                        Lifetimes = default
                    }
                };
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Query(captured: malformed), ErrorCode.Corruption);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Statistics(malformed), ErrorCode.Corruption);
            });
            await QueryLifetimeNativeAssertions.ColdReplayAsync(flow);
        });
}
