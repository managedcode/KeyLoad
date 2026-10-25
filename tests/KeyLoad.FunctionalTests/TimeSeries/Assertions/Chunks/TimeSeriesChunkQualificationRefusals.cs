using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.FunctionalTests.GraphTraversal;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class TimeSeriesChunkQualificationRefusals
{
    internal static Task RunAsync(TimeSeriesChunkQualificationFixture fixture, DatabaseEngine owner,
        ZoneTreeStore store, bool scopeDenied, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            foreach (var partition in fixture.Partitions)
            {
                var request = TimeSeriesChunkQualificationData.Request(partition, fixture.Window) with
                {
                    TagPointer = TimeSeriesChunkQualificationData.PrivatePath,
                    TagValue = "alpha"
                };
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => owner.ReadSampleChunkWindow(TimeSeriesChunkQualificationData.Reader, request, token));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(failure.Message).IsEqualTo(scopeDenied ? TimeSeriesChunkQualificationData.ScopeDenied
                    : TimeSeriesChunkQualificationData.FieldUseDenied);
            }
        });
    internal static Task BoundAsync(TimeSeriesChunkQualificationFixture fixture, DatabaseEngine owner, ZoneTreeStore store, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var request = TimeSeriesChunkQualificationData.Request(fixture.Partitions[TimeSeriesChunkQualificationData.SiblingScope], fixture.Window) with
            {
                Limit = TimeSeriesChunkQualificationData.RefusedReadLimit,
                MinimumToken = fixture.Originals[TimeSeriesChunkQualificationData.SiblingScope].Receipt.Token
            };
            var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.ReadSampleChunkWindow(TimeSeriesChunkQualificationData.Reader, request, token));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(error.Message).IsEqualTo(TimeSeriesChunkQualificationData.ReadBoundExceeded);
        });
    internal static Task TagRepairAsync(TimeSeriesChunkQualificationFixture fixture, DatabaseEngine owner, ZoneTreeStore store, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var request = TimeSeriesChunkQualificationData.Request(fixture.Partitions[TimeSeriesChunkQualificationData.SiblingScope], fixture.Window) with
            {
                MinimumToken = fixture.Originals[TimeSeriesChunkQualificationData.SiblingScope].Receipt.Token,
                TagPointer = TimeSeriesChunkQualificationData.PrivatePath,
                TagValue = "beta"
            };
            await RequiredProcessingPolicyAssertions.EqualAsync(TimeSeriesChunkQualificationData.Chunk(fixture.Window,
                true, fixture.Phase, true, store.Position), owner.ReadSampleChunkWindow(TimeSeriesChunkQualificationData.Reader, request, token));
        });
    internal static Task CancellationAsync(TimeSeriesChunkQualificationFixture fixture, DatabaseEngine owner, ZoneTreeStore store)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            var original = cancellation.Token;
            var failure = Assert.ThrowsExactly<OperationCanceledException>(() => owner.ReadSampleChunkWindow(
                TimeSeriesChunkQualificationData.Reader, TimeSeriesChunkQualificationData.Request(fixture.Partitions[TimeSeriesChunkQualificationData.SiblingScope], fixture.Window), original));
            await Assert.That(failure.CancellationToken).IsEqualTo(original);
        });
}
