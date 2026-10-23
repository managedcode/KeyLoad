using System.Text.Json;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchStatisticsAssertions
{
    private const int CorpusDocuments = 2;
    private const int CorpusLength = 2;
    private const double UnitFrequencyScale = 2.2;
    private const double UnitLengthScale = 1.2;

    internal static async Task HealthyAsync(QueryFixture fixture, CancellationToken token)
    {
        var source = DistributedSearchStatisticsFlow.Capture(fixture.Source,
            QueryDataset.Local, fixture.SourceOwner, token);
        var destination = DistributedSearchStatisticsFlow.Capture(fixture.Destination,
            QueryFixture.Partition, fixture.DestinationOwner, token);
        var statistics = DistributedSearchStatisticsFlow.Combine(fixture.Source, source, destination, token);
        var expectedStatistics = new DistributedTextStatistics(["source", "destination"],
            CorpusDocuments, CorpusLength, [1, 1]);
        await Assert.That(JsonSerializer.Serialize(statistics, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expectedStatistics, JsonDefaults.Options));
        var sourceRows = DistributedSearchStatisticsFlow.Rank(fixture.Source, source, statistics, token);
        var destinationRows = DistributedSearchStatisticsFlow.Rank(fixture.Destination, destination, statistics, token);
        RankedDocument[] actual = [.. destinationRows, .. sourceRows];
        RankedDocument[] expected =
        [
            new(new(fixture.ExpectedDestinationReference, QueryFixture.First,
                QueryFixture.ProjectedJson, true, [QueryFixture.Secret]), Math.Log(CorpusDocuments) * UnitFrequencyScale / (1 + UnitLengthScale)),
            new(new(fixture.ExpectedSourceReference, QueryFixture.First,
                QueryDataset.SourceProjected, true, [QueryFixture.Secret]), Math.Log(CorpusDocuments) * UnitFrequencyScale / (1 + UnitLengthScale))
        ];
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }
}
