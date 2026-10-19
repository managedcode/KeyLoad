using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryLifetimeAssertions
{
    internal static async Task HealthyAsync(QueryLifetimeFlow flow)
    {
        flow.Fixture.RequireOriginalResourceBindings();
        var expected = new PartitionQueryCandidate(QueryLifetimeProtocol.Version,
            flow.Fixture.ExpectedDestinationReference, new(QueryFixture.DocumentId,
                QueryFixture.First, QueryFixture.ProjectedJson, true,
                [QueryFixture.Secret]), []);
        var result = flow.Query();
        await Assert.That(result.Candidates.Length).IsEqualTo(QueryLifetimeProtocol.Limit);
        await Assert.That(JsonDefaults.Serialize(result.Candidates.Single()).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var statistics = flow.Statistics().Witness.Statistics;
        var expectedStatistics = new DistributedTextStatistics([QueryLifetimeProtocol.Text], QueryLifetimeProtocol.Limit,
            QueryLifetimeProtocol.Limit, [QueryLifetimeProtocol.Limit]);
        await Assert.That(JsonDefaults.Serialize(statistics).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedStatistics))).IsTrue();
        await QueryLifetimePhaseRoundTrips.VerifyAsync(flow);
    }

    internal static async Task UnchangedAsync(QueryLifetimeFlow flow, Func<Task> operation)
    {
        var source = QueryFixtureAssertions.Image(flow.Fixture.Source);
        var target = QueryFixtureAssertions.Image(flow.Fixture.Destination);
        var sourcePosition = flow.Fixture.Source.Store.Position;
        var targetPosition = flow.Fixture.Destination.Store.Position;
        await operation().ConfigureAwait(false);
        await QueryFixtureAssertions.StateAsync(flow.Fixture, source, target, sourcePosition, targetPosition);
    }

    internal static async Task ColdReplayAsync(QueryLifetimeFlow flow)
    {
        await QueryFixtureAssertions.ReceiptAsync(flow.Fixture);
        var receipt = OrleansSerialization.Serialize(flow.Fixture.Receipt);
        await UnchangedAsync(flow, async () =>
        {
            var replay = flow.Fixture.Destination.Database.Apply(flow.Fixture.Write).Get<CommitReceipt>();
            await Assert.That(OrleansSerialization.Serialize(replay).AsSpan().SequenceEqual(receipt)).IsTrue();
            await HealthyAsync(flow);
        });
        for (var index = QueryLifetimeProtocol.NoRows; index < QueryLifetimeProtocol.ColdCount; index++)
        {
            var source = QueryFixtureAssertions.Image(flow.Fixture.Source);
            var target = QueryFixtureAssertions.Image(flow.Fixture.Destination);
            await flow.Fixture.Source.ReopenAsync();
            await flow.Fixture.Destination.ReopenAsync();
            await Assert.That(QueryFixtureAssertions.Image(flow.Fixture.Source).SequenceEqual(source)).IsTrue();
            await Assert.That(QueryFixtureAssertions.Image(flow.Fixture.Destination).SequenceEqual(target)).IsTrue();
            await UnchangedAsync(flow, async () =>
            {
                var replay = flow.Fixture.Destination.Database.Apply(flow.Fixture.Write).Get<CommitReceipt>();
                await Assert.That(OrleansSerialization.Serialize(replay).AsSpan().SequenceEqual(receipt)).IsTrue();
                await HealthyAsync(flow);
            });
        }
    }

    internal static async Task RefusedAsync(Action read, ErrorCode code, string? detail = null)
    {
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
        {
            read();
            return Task.CompletedTask;
        }) ?? throw new InvalidOperationException(QueryLifetimeProtocol.MissingFailure);
        await Assert.That(failure.Code).IsEqualTo(code);
        if (detail is not null)
        {
            await Assert.That(failure.Message).IsEqualTo(detail);
        }
    }
}
