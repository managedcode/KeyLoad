using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryHostedSearchAssertions
{
    private const int MaximumRecords = 4096;
    private const int FirstRank = 1;
    private const int SecondRank = 2;
    private const int HexCharactersPerByte = 2;
    private const string AccessPath = "distributedCanonicalSearch";

    internal static QueryHostedStoreImage[] Capture(RequestCqrsHostedCluster cluster)
        => cluster.Nodes.Select(node => node.Database.Store.Read(view =>
        {
            var page = view.Scan([], MaximumRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The complete execution query image exceeds its record bound.");
            }
            var records = page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
                Convert.ToHexString(record.Value.Span)).ToArray();
            var applied = view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
                ? OrleansSerialization.Deserialize<long>(bytes) : default(long);
            return new QueryHostedStoreImage(applied, node.Database.Store.Position, records);
        })).ToArray();

    internal static async Task StateAsync(RequestCqrsHostedCluster cluster, QueryHostedStoreImage[] expected)
    {
        var actual = Capture(cluster);
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].AppliedPosition).IsEqualTo(expected[index].AppliedPosition);
            await Assert.That(actual[index].StorePosition).IsEqualTo(expected[index].StorePosition);
            await Assert.That(actual[index].Records.SequenceEqual(expected[index].Records)).IsTrue();
        }
    }

    internal static async Task PageAsync(DistributedSearchPage page, QueryHostedSearchDataset data,
        QueryHostedStoreImage image)
    {
        var lifetime = new EntityRefLifetime(data.Creation.Incarnation.Value);
        RankedDocument[] expected =
        [
            new(new(new(data.First, QueryHostedSearchDataset.Collection, QueryHostedSearchDataset.DocumentId)
                { Lifetime = lifetime }, QueryHostedSearchDataset.Version,
                QueryHostedSearchDataset.DestinationProjected, true, [QueryHostedSearchDataset.Secret]),
                1d / (QueryHostedSearchDataset.FusionConstant + FirstRank)),
            new(new(new(data.Second, QueryHostedSearchDataset.Collection, QueryHostedSearchDataset.DocumentId)
                { Lifetime = lifetime }, QueryHostedSearchDataset.Version,
                QueryHostedSearchDataset.SourceProjected, true, [QueryHostedSearchDataset.Secret]),
                1d / (QueryHostedSearchDataset.FusionConstant + SecondRank))
        ];
        PartitionQueryLeafWitness[] leaves =
        [
            new(data.First, image.StorePosition, QueryHostedSearchDataset.Version, QueryHostedSearchDataset.Version, AccessPath),
            new(data.Second, image.StorePosition, QueryHostedSearchDataset.Version, QueryHostedSearchDataset.Version, AccessPath)
        ];
        await Assert.That(JsonDefaults.Serialize(page.Hits).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected.ToImmutableArray()))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(page.Leaves).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(leaves.ToImmutableArray()))).IsTrue();
        await Assert.That(page.Version).IsEqualTo(QueryHostedSearchDataset.Version);
        await Assert.That(page.Complete).IsTrue();
        await Assert.That(page.StatisticsEpoch.Length).IsEqualTo(SHA256.HashSizeInBytes * HexCharactersPerByte);
        await Assert.That(page.StatisticsEpoch.All(char.IsAsciiHexDigitLower)).IsTrue();
    }

    internal static async Task HealthyAsync(RequestCqrsHostedCluster cluster, QueryHostedSearchDataset data,
        QueryHostedStoreImage[] images, CancellationToken token)
    {
        for (var index = 0; index < cluster.Nodes.Length; index++)
        {
            var page = QueryHostedSearchRead.Page(await QueryHostedSearchRead.ExecuteAsync(cluster.Nodes[index],
                data, Guid.NewGuid(), token).ConfigureAwait(false));
            await PageAsync(page, data, images[index]).ConfigureAwait(false);
        }
        await StateAsync(cluster, images).ConfigureAwait(false);
    }
}
