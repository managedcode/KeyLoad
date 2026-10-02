using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleOrderingTests
{
    private const string Metrics = "metrics";
    private const string RootIdentity = "root";
    private const string CpuSeries = "cpu";
    private const string LaterSampleId = "later";
    private const string EarlierSampleId = "earlier";
    private const double LaterSampleValue = 2;
    private const double EarlierSampleValue = 1;
    private static readonly TimeSpan SampleOffset = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReadWindow = TimeSpan.FromMinutes(1);

    [Test]
    public async Task OutOfOrderSamplesStayOrderedAndSampleIdIsIdempotent()
    {
        using var database = new TestDatabase();
        database.Configure(Metrics, ResourceKind.TimeSeries);
        var now = TimeProvider.System.GetUtcNow();
        database.Commit(new AppendSamples(Metrics, CpuSeries,
            [new(LaterSampleId, now, LaterSampleValue), new(EarlierSampleId, now.Subtract(SampleOffset), EarlierSampleValue)]));
        database.Commit(new AppendSamples(Metrics, CpuSeries, [new(LaterSampleId, now, LaterSampleValue)]));

        var samples = database.Database.ReadSamples(RootIdentity, database.Partition, Metrics, CpuSeries,
            now.Subtract(ReadWindow), now.Add(ReadWindow)).Select(row => row.Sample.EventId);
        await Assert.That(samples).IsEquivalentTo(new[] { EarlierSampleId, LaterSampleId }, CollectionOrdering.Matching);
    }
}
