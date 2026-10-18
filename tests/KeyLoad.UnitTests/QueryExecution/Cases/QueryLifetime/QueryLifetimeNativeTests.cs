using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryLifetimeNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BothAuthoritiesRestrictRowsBeforeQueryPredicatesAndStatisticsThenRepairReplayAndTwoColdOwners(bool sourceNarrower)
        => await QueryNativeFixtureAssertions.RunAsync(async fixture =>
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
        => await QueryNativeFixtureAssertions.RunAsync(async fixture =>
        {
            var flow = new QueryLifetimeNativeFlow(fixture, sourceNarrower);
            flow.Seed(restrictedRows: false);
            await QueryLifetimeNativeAssertions.UnchangedAsync(flow, () => QueryLifetimeNativeAssertions.HealthyAsync(flow));
            await QueryLifetimeNativeAssertions.UnchangedAsync(flow, async () =>
            {
                var selected = flow.Query(QueryNativeFixture.Secret).Candidates.Single().Row;
                var expected = new QueryRow(QueryNativeFixture.DocumentId, QueryNativeFixture.First,
                    QueryLifetimeNativeProtocol.NullSecretJson, true, [QueryNativeFixture.Secret]);
                await Assert.That(JsonDefaults.Serialize(selected).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            await QueryLifetimeNativeAssertions.UnchangedAsync(flow, async () =>
            {
                var filter = new Comparison(new FieldOperand(QueryNativeFixture.Secret), QueryLifetimeNativeProtocol.EqualsOperator,
                    new ValueOperand(System.Text.Json.JsonSerializer.SerializeToElement(QueryLifetimeNativeProtocol.PrivateValue)));
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Query(filter: filter),
                    ErrorCode.PermissionDenied, QueryLifetimeNativeProtocol.UseDenied);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Statistics(
                    field: QueryNativeFixture.Secret, text: QueryLifetimeNativeProtocol.PrivateTerm),
                    ErrorCode.PermissionDenied, QueryLifetimeNativeProtocol.UseDenied);
                var unknown = flow.Query(QueryLifetimeNativeProtocol.MissingPath).Candidates.Single().Row;
                var expected = new QueryRow(QueryNativeFixture.DocumentId, QueryNativeFixture.First,
                    QueryLifetimeNativeProtocol.NullSecretJson, false, []);
                await Assert.That(JsonDefaults.Serialize(unknown).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            flow.HideNarrowSchema();
            await QueryLifetimeNativeAssertions.UnchangedAsync(flow, async () =>
            {
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Query(QueryNativeFixture.Secret),
                    ErrorCode.Validation, QueryLifetimeNativeProtocol.SchemaDenied);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Query(QueryLifetimeNativeProtocol.MissingPath),
                    ErrorCode.Validation, QueryLifetimeNativeProtocol.SchemaDenied);
                await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = flow.Statistics(),
                    ErrorCode.Validation, QueryLifetimeNativeProtocol.SchemaDenied);
                var hidden = flow.Query().Candidates.Single().Row;
                var expected = new QueryRow(QueryNativeFixture.DocumentId, QueryNativeFixture.First,
                    QueryNativeFixture.ProjectedJson, true, []);
                await Assert.That(JsonDefaults.Serialize(hidden).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            flow.RepairSchema();
            await QueryLifetimeNativeAssertions.ColdReplayAsync(flow);
        });

    [Test]
    public async Task ActualIndependentLifetimeCannotSatisfyAnotherResourceLeafThenHealthyReplaySurvivesTwoColdOwners()
        => await QueryNativeFixtureAssertions.RunAsync(async fixture =>
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
