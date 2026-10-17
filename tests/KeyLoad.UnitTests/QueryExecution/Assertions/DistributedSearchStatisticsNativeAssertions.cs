using System.Text.Json;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchStatisticsNativeAssertions
{
    private const int CorpusDocuments = 2;
    private const int CorpusLength = 2;
    private const int TermDocuments = 1;
    private const double UnitFrequencyScale = 2.2;
    private const double UnitLengthScale = 1.2;

    internal static async Task HealthyAsync(RemoteDocumentNativeFixture fixture, CancellationToken token)
    {
        var source = DistributedSearchStatisticsNativeFlow.Capture(fixture.Source,
            RemotePartitionQueryNativeFlow.Local, PhysicalOwnerDirectoryWholeFlow.Control.Owner, token);
        var destination = DistributedSearchStatisticsNativeFlow.Capture(fixture.Destination,
            RemoteDocumentNativeFixture.Partition, PhysicalOwnerDirectoryWholeFlow.Destination.Owner, token);
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
            new(new(RemoteDocumentNativeFixture.Reference, RemoteDocumentNativeFixture.First,
                RemoteDocumentNativeFixture.ProjectedJson, true, [RemoteDocumentNativeFixture.Secret]), Math.Log(CorpusDocuments) * UnitFrequencyScale / (TermDocuments + UnitLengthScale)),
            new(new(new(RemotePartitionQueryNativeFlow.Local, RemoteDocumentNativeFixture.Collection,
                RemoteDocumentNativeFixture.DocumentId), RemoteDocumentNativeFixture.First,
                RemotePartitionQueryNativeFlow.SourceProjected, true, [RemoteDocumentNativeFixture.Secret]), Math.Log(CorpusDocuments) * UnitFrequencyScale / (TermDocuments + UnitLengthScale))
        ];
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }
}
