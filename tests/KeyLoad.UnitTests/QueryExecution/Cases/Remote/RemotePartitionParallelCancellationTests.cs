
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class RemotePartitionParallelCancellationTests
{
    private const int IndependentOwners = 2;
    private const string Missing = "The native parallel caller cancellation did not retain its original failure.";

    [Test]
    public Task BothIndependentNativeLeafReadsAreObservedBeforeCallerCancellationAndJoinedBeforeLiteralHealthyMerge()
        => QueryNativeFixtureAssertions.RunAsync(async fixture =>
        {
            var fixtureToken = TestContext.Current!.Execution.CancellationToken;
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(fixtureToken);
            var barrier = new RemotePartitionNativeReadBarrier(caller, fixtureToken);
            var sourceClock = new RemotePartitionNativeReadClock(
                () => fixture.Source.Store.GetReadDiagnostics().RangeExaminedBytes, barrier);
            var destinationClock = new RemotePartitionNativeReadClock(
                () => fixture.Destination.Store.GetReadDiagnostics().RangeExaminedBytes, barrier);
            var flow = new QueryNativePartitionFlow(fixture, sourceClock, destinationClock);
            flow.Seed();
            flow.GrantDestination();
            var source = QueryNativeFixtureAssertions.Image(fixture.Source);
            var destination = QueryNativeFixtureAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            PartitionQueryPageV1? page = null;
            OperationCanceledException error;
            try
            {
                error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
                    page = await flow.ExecuteAsync(caller.Token)) ?? throw new InvalidOperationException(Missing);
            }
            finally { sourceClock.Disarm(); destinationClock.Disarm(); }
            await Assert.That(error.CancellationToken).IsEqualTo(caller.Token);
            await Assert.That(barrier.Arrivals).IsEqualTo(IndependentOwners);
            await Assert.That(sourceClock.Triggered).IsTrue();
            await Assert.That(destinationClock.Triggered).IsTrue();
            await Assert.That(page).IsNull();
            await QueryNativeFixtureAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
            await QueryNativeFixtureAssertions.PageAsync(await flow.ExecuteAsync(fixtureToken), fixture);
            await QueryNativeFixtureAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
        });
}
