using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Authorization;
using KeyLoad.UnitTests.GraphTraversal;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class TimeSeriesChunkQualificationReads
{
    internal static Task RunAsync(TimeSeriesChunkQualificationFixture fixture, DatabaseEngine owner, ZoneTreeStore store,
        bool reader, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var failures = new List<Exception>();
            for (var index = 0; index < fixture.Partitions.Length; index++)
            {
                await ServerFailureObserver.ObserveAsync(() => ScopeAsync(fixture, owner, store.Position,
                    index == TimeSeriesChunkQualificationData.SiblingScope, reader, token), failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        });
    private static async Task ScopeAsync(TimeSeriesChunkQualificationFixture fixture, DatabaseEngine owner,
        long cut, bool sibling, bool reader, CancellationToken token)
    {
        var partition = fixture.Partitions[sibling ? TimeSeriesChunkQualificationData.SiblingScope : TimeSeriesChunkQualificationData.PrimaryScope];
        var principal = reader ? TimeSeriesChunkQualificationData.Reader : TimeSeriesChunkQualificationData.RootPrincipal;
        var expectedRows = TimeSeriesChunkQualificationData.Rows(sibling, fixture.Phase, reader);
        var request = TimeSeriesChunkQualificationData.Request(partition, fixture.Window) with
        {
            MinimumToken = fixture.Originals[sibling ? TimeSeriesChunkQualificationData.SiblingScope : TimeSeriesChunkQualificationData.PrimaryScope].Receipt.Token
        };
        if (!sibling && fixture.Phase == TimeSeriesChunkQualificationData.DroppedPhase)
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.ReadSampleChunkWindow(principal, request, token));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
            await Assert.That(error.Message).IsEqualTo(TimeSeriesChunkQualificationData.Missing);
        }
        else
        {
            var chunk = owner.ReadSampleChunkWindow(principal, request, token);
            await RequiredProcessingPolicyAssertions.EqualAsync(TimeSeriesChunkQualificationData.Chunk(fixture.Window,
                sibling, fixture.Phase, reader, cut), chunk);
            await BitsAsync(expectedRows, chunk.Records.ToArray());
        }
        var raw = owner.ReadSamples(principal, partition, TimeSeriesChunkQualificationData.Set,
            TimeSeriesChunkQualificationData.Series, TimeSeriesChunkQualificationData.From,
            TimeSeriesChunkQualificationData.Until, TimeSeriesChunkQualificationData.ReadLimit, token);
        await RequiredProcessingPolicyAssertions.EqualAsync(expectedRows, raw);
        await BitsAsync(expectedRows, raw);
        await RequiredProcessingPolicyAssertions.EqualAsync(new LatestSampleResult(expectedRows.LastOrDefault()),
            owner.ReadLatestSample(principal, new(partition, TimeSeriesChunkQualificationData.Set,
                TimeSeriesChunkQualificationData.Series), token));
        await RequiredProcessingPolicyAssertions.EqualAsync(TimeSeriesChunkQualificationData.Aggregate(sibling, fixture.Phase),
            owner.AggregateSamples(principal, new(partition, TimeSeriesChunkQualificationData.Set, TimeSeriesChunkQualificationData.Series,
                TimeSeriesChunkQualificationData.From, TimeSeriesChunkQualificationData.Until), token));
        var window = new AggregateSampleWindowsRequest(partition, TimeSeriesChunkQualificationData.Set,
            TimeSeriesChunkQualificationData.Series, TimeSeriesChunkQualificationData.From, TimeSeriesChunkQualificationData.Until,
            TimeSeriesChunkQualificationData.WindowWidth, TimeSeriesChunkQualificationData.ReadLimit, TimeSeriesChunkQualificationData.DenseWindowCount);
        await RequiredProcessingPolicyAssertions.EqualAsync(new SampleAggregateWindowsResult([new(TimeSeriesChunkQualificationData.From,
            TimeSeriesChunkQualificationData.Until, TimeSeriesChunkQualificationData.Aggregate(sibling, fixture.Phase))]),
            owner.AggregateSampleWindows(principal, window, token));
        await RequiredProcessingPolicyAssertions.EqualAsync(TimeSeriesChunkQualificationData.Retention(sibling, fixture.Phase,
            fixture.Purged, fixture.HasMore), owner.ReadSampleRetention(principal,
                new(partition, TimeSeriesChunkQualificationData.Set, TimeSeriesChunkQualificationData.Series), token));
    }
    private static async Task BitsAsync(SampleRecord[] expected, SampleRecord[] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(BitConverter.DoubleToInt64Bits(actual[index].Sample.Value))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(expected[index].Sample.Value));
            await Assert.That(actual[index].Sample.Timestamp.Ticks).IsEqualTo(expected[index].Sample.Timestamp.Ticks);
            await Assert.That(actual[index].Sample.Timestamp.Offset).IsEqualTo(expected[index].Sample.Timestamp.Offset);
        }
    }
}
