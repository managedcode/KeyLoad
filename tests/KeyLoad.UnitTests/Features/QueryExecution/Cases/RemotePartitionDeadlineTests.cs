using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class RemotePartitionDeadlineTests
{
    private const int PastDeadlineSeconds = 1;
    private const string DeadlineDetail = "The read execution deadline is exceeded.";
    private const string Missing = "The original receiving deadline failure is unavailable.";

    [Test]
    public Task ActualReceivingNativeWorkExceedsOriginalDeadlineThenJoinedIndependentOwnersReturnLiteralHealthy()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var token = TestContext.Current!.Execution.CancellationToken;
            var clock = new QueryObservedWorkClock();
            var flow = new RemotePartitionQueryNativeFlow(fixture, destinationClock: clock);
            flow.Seed();
            flow.GrantDestination();
            var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            var examined = fixture.Destination.Store.GetReadDiagnostics().RangeExaminedBytes;
            clock.Arm(() => fixture.Destination.Store.GetReadDiagnostics().RangeExaminedBytes > examined,
                () => clock.Advance(TimeSpan.FromSeconds(
                    fixture.Destination.Database.Limits.QueryDeadlineSeconds + PastDeadlineSeconds)));
            PartitionQueryPageV1? page = null;
            KeyLoadException error;
            try
            {
                error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
                    page = await flow.ExecuteAsync(token)) ?? throw new InvalidOperationException(Missing);
            }
            finally
            { clock.Disarm(); }
            await Assert.That(clock.Triggered).IsTrue();
            await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(error.Message).IsEqualTo(DeadlineDetail);
            await Assert.That(page).IsNull();
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
            await RemotePartitionQueryNativeAssertions.PageAsync(await flow.ExecuteAsync(token), fixture);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
        });
}
