using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryLifetimeNativeAssertions
{
    internal static async Task HealthyAsync(QueryLifetimeNativeFlow flow)
    {
        var result = flow.Query();
        await Assert.That(result.Candidates.Length).IsEqualTo(QueryLifetimeNativeProtocol.Limit);
        var expected = new PartitionQueryCandidateV1(QueryLifetimeNativeProtocol.Version,
            RemoteDocumentNativeFixture.Reference, new(RemoteDocumentNativeFixture.DocumentId,
                RemoteDocumentNativeFixture.First, RemoteDocumentNativeFixture.ProjectedJson, true,
                [RemoteDocumentNativeFixture.Secret]), []);
        await Assert.That(JsonDefaults.Serialize(result.Candidates.Single()).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var statistics = flow.Statistics().Witness.Statistics;
        var expectedStatistics = new DistributedTextStatisticsV1([QueryLifetimeNativeProtocol.Text], QueryLifetimeNativeProtocol.Limit,
            QueryLifetimeNativeProtocol.Limit, [QueryLifetimeNativeProtocol.Limit]);
        await Assert.That(JsonDefaults.Serialize(statistics).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedStatistics))).IsTrue();
        await QueryLifetimeNativePhaseRoundTrips.VerifyAsync(flow);
    }

    internal static async Task UnchangedAsync(QueryLifetimeNativeFlow flow, Func<Task> operation)
    {
        var source = RemoteDocumentNativeAssertions.Image(flow.Fixture.Source);
        var target = RemoteDocumentNativeAssertions.Image(flow.Fixture.Destination);
        var sourcePosition = flow.Fixture.Source.Store.Position;
        var targetPosition = flow.Fixture.Destination.Store.Position;
        await operation().ConfigureAwait(false);
        await RemotePartitionQueryNativeAssertions.StateAsync(flow.Fixture, source, target, sourcePosition, targetPosition);
    }

    internal static async Task ColdReplayAsync(QueryLifetimeNativeFlow flow)
    {
        await RemoteDocumentNativeAssertions.ReceiptAsync(flow.Fixture);
        var receipt = NativeSerialization.Serialize(flow.Fixture.Receipt);
        await UnchangedAsync(flow, async () =>
        {
            var replay = flow.Fixture.Destination.Database.Apply(flow.Fixture.Write).Get<CommitReceipt>();
            await Assert.That(NativeSerialization.Serialize(replay).AsSpan().SequenceEqual(receipt)).IsTrue();
            await HealthyAsync(flow);
        });
        for (var index = QueryLifetimeNativeProtocol.NoRows; index < QueryLifetimeNativeProtocol.ColdCount; index++)
        {
            var source = RemoteDocumentNativeAssertions.Image(flow.Fixture.Source);
            var target = RemoteDocumentNativeAssertions.Image(flow.Fixture.Destination);
            flow.Fixture.Source.Reopen();
            flow.Fixture.Destination.Reopen();
            await Assert.That(RemoteDocumentNativeAssertions.Image(flow.Fixture.Source).SequenceEqual(source)).IsTrue();
            await Assert.That(RemoteDocumentNativeAssertions.Image(flow.Fixture.Destination).SequenceEqual(target)).IsTrue();
            await UnchangedAsync(flow, async () =>
            {
                var replay = flow.Fixture.Destination.Database.Apply(flow.Fixture.Write).Get<CommitReceipt>();
                await Assert.That(NativeSerialization.Serialize(replay).AsSpan().SequenceEqual(receipt)).IsTrue();
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
        }) ?? throw new InvalidOperationException(QueryLifetimeNativeProtocol.MissingFailure);
        await Assert.That(failure.Code).IsEqualTo(code);
        if (detail is not null)
        {
            await Assert.That(failure.Message).IsEqualTo(detail);
        }
    }
}
