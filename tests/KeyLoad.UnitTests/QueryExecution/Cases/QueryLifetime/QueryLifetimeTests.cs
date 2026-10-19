using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryLifetimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BothAuthoritiesRestrictRowsBeforeQueryPredicatesAndStatisticsThenRepairReplayAndTwoColdOwners(bool sourceNarrower)
        => await QueryFixtureAssertions.RunAsync(async fixture =>
        {
            var flow = new QueryLifetimeFlow(fixture, sourceNarrower);
            flow.Seed(restrictedRows: true);
            await QueryLifetimeAssertions.UnchangedAsync(flow, async () =>
            {
                await Assert.That(flow.Query().Candidates.Length).IsEqualTo(QueryLifetimeProtocol.NoRows);
                var expected = new KeyLoad.Query.Features.QueryExecution.DistributedTextStatistics(
                    [QueryLifetimeProtocol.Text], QueryLifetimeProtocol.NoRows, QueryLifetimeProtocol.NoRows,
                    [QueryLifetimeProtocol.NoRows]);
                await Assert.That(JsonDefaults.Serialize(flow.Statistics().Witness.Statistics).AsSpan()
                    .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            flow.RepairRows();
            await QueryLifetimeAssertions.ColdReplayAsync(flow);
        });

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BothAuthoritiesProjectThenHideKnownAndUnknownSchemaBeforeFieldBindingWithoutChangingState(bool sourceNarrower)
        => await QueryFixtureAssertions.RunAsync(async fixture =>
        {
            var flow = new QueryLifetimeFlow(fixture, sourceNarrower);
            flow.Seed(restrictedRows: false);
            await QueryLifetimeAssertions.UnchangedAsync(flow, () => QueryLifetimeAssertions.HealthyAsync(flow));
            await QueryLifetimeAssertions.UnchangedAsync(flow, async () =>
            {
                var selected = flow.Query(QueryFixture.Secret).Candidates.Single().Row;
                var expected = new QueryRow(QueryFixture.DocumentId, QueryFixture.First,
                    QueryLifetimeProtocol.NullSecretJson, true, [QueryFixture.Secret]);
                await Assert.That(JsonDefaults.Serialize(selected).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            await QueryLifetimeAssertions.UnchangedAsync(flow, async () =>
            {
                var filter = new Comparison(new FieldOperand(QueryFixture.Secret), QueryLifetimeProtocol.EqualsOperator,
                    new ValueOperand(System.Text.Json.JsonSerializer.SerializeToElement(QueryLifetimeProtocol.PrivateValue)));
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Query(filter: filter),
                    ErrorCode.PermissionDenied, QueryLifetimeProtocol.UseDenied);
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Statistics(
                    field: QueryFixture.Secret, text: QueryLifetimeProtocol.PrivateTerm),
                    ErrorCode.PermissionDenied, QueryLifetimeProtocol.UseDenied);
                var unknown = flow.Query(QueryLifetimeProtocol.MissingPath).Candidates.Single().Row;
                var expected = new QueryRow(QueryFixture.DocumentId, QueryFixture.First,
                    QueryLifetimeProtocol.NullSecretJson, false, []);
                await Assert.That(JsonDefaults.Serialize(unknown).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            flow.HideNarrowSchema();
            await QueryLifetimeAssertions.UnchangedAsync(flow, async () =>
            {
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Query(QueryFixture.Secret),
                    ErrorCode.Validation, QueryLifetimeProtocol.SchemaDenied);
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Query(QueryLifetimeProtocol.MissingPath),
                    ErrorCode.Validation, QueryLifetimeProtocol.SchemaDenied);
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Statistics(),
                    ErrorCode.Validation, QueryLifetimeProtocol.SchemaDenied);
                var hidden = flow.Query().Candidates.Single().Row;
                var expected = new QueryRow(QueryFixture.DocumentId, QueryFixture.First,
                    QueryFixture.ProjectedJson, true, []);
                await Assert.That(JsonDefaults.Serialize(hidden).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            });
            flow.RepairSchema();
            await QueryLifetimeAssertions.ColdReplayAsync(flow);
        });

    [Test]
    public async Task ActualIndependentLifetimeCannotSatisfyAnotherResourceLeafThenHealthyReplaySurvivesTwoColdOwners()
        => await QueryFixtureAssertions.RunAsync(async fixture =>
        {
            var flow = new QueryLifetimeFlow(fixture, sourceNarrower: true);
            flow.Seed(restrictedRows: false);
            var original = flow.Capture();
            var other = flow.CreateOtherLifetime();
            var wrong = original with
            {
                Coverage = other
            };
            await QueryLifetimeAssertions.UnchangedAsync(flow, async () =>
            {
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Query(captured: wrong), ErrorCode.Corruption);
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Statistics(wrong), ErrorCode.Corruption);
                var missing = original with
                {
                    Coverage = null!
                };
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Query(captured: missing), ErrorCode.Corruption);
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Statistics(missing), ErrorCode.Corruption);
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Query(captured: missing, runtimeRoundTrip: false),
                    ErrorCode.OwnershipLost);
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Statistics(missing, runtimeRoundTrip: false),
                    ErrorCode.OwnershipLost);
                var malformed = original with
                {
                    Coverage = original.Coverage with
                    {
                        Lifetimes = default
                    }
                };
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Query(captured: malformed), ErrorCode.Corruption);
                await QueryLifetimeAssertions.RefusedAsync(() => _ = flow.Statistics(malformed), ErrorCode.Corruption);
            });
            await QueryLifetimeAssertions.ColdReplayAsync(flow);
        });
}
