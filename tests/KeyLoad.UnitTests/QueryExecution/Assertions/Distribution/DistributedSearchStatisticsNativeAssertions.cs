using System.Text.Json;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchStatisticsNativeAssertions
{
    private const int CorpusDocuments = 2;
    private const int CorpusLength = 2;
    private const int TermDocuments = 1;
    private const double UnitFrequencyScale = 2.2;
    private const double UnitLengthScale = 1.2;

    internal static async Task HealthyAsync(QueryNativeFixture fixture, CancellationToken token)
    {
        var source = DistributedSearchStatisticsNativeFlow.Capture(fixture.Source,
            QueryNativePartitionFlow.Local, fixture.SourceOwner, token);
        var destination = DistributedSearchStatisticsNativeFlow.Capture(fixture.Destination,
            QueryNativeFixture.Partition, fixture.DestinationOwner, token);
        var statistics = DistributedSearchStatisticsNativeFlow.Combine(fixture.Source, source, destination, token);
        var expectedStatistics = new DistributedTextStatisticsV1(["source", "destination"],
            CorpusDocuments, CorpusLength, [TermDocuments, TermDocuments]);
        await Assert.That(JsonSerializer.Serialize(statistics, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expectedStatistics, JsonDefaults.Options));
        var sourceRows = DistributedSearchStatisticsNativeFlow.Rank(fixture.Source, source, statistics, token);
        var destinationRows = DistributedSearchStatisticsNativeFlow.Rank(fixture.Destination, destination, statistics, token);
        RankedDocument[] actual = [.. destinationRows, .. sourceRows];
        RankedDocument[] expected =
        [
            new(new(fixture.ExpectedDestinationReference, QueryNativeFixture.First,
                QueryNativeFixture.ProjectedJson, true, [QueryNativeFixture.Secret]), Math.Log(CorpusDocuments) * UnitFrequencyScale / (TermDocuments + UnitLengthScale)),
            new(new(fixture.ExpectedSourceReference, QueryNativeFixture.First,
                QueryNativePartitionFlow.SourceProjected, true, [QueryNativeFixture.Secret]), Math.Log(CorpusDocuments) * UnitFrequencyScale / (TermDocuments + UnitLengthScale))
        ];
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }
}
