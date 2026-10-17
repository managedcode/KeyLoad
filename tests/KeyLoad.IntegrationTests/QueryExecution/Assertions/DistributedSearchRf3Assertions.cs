using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedSearchRf3Assertions
{
    private const int FusionConstant = 60;
    private const int FirstRank = 1;
    private const int SecondRank = 2;
    private const int ThirdRank = 3;
    private const int FirstLeaf = 0;
    private const int SecondLeaf = 1;
    private const int LeafCount = 2;
    private const int Sha256HexCharacters = 64;
    private const double TextWeight = 2;
    private const double VectorWeight = 1;
    private const string AccessPath = "distributedCanonicalSearchV1";
    private const string SchemaNames = "complete,hits,leaves,statisticsEpoch,version";

    internal static async Task PageAsync(DistributedSearchPageV1 actual, DistributedSearchRf3Seed seed,
        long sourceUpperCut, long destinationUpperCut, long destinationEpoch)
    {
        await Assert.That(actual.Version).IsEqualTo(DistributedSearchRf3Seed.Version);
        await Assert.That(actual.Complete).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual.Hits).AsSpan().SequenceEqual(JsonDefaults.Serialize(Expected()))).IsTrue();
        await WitnessesAsync(actual, seed, sourceUpperCut, destinationUpperCut, destinationEpoch);
    }

    internal static async Task EmptyPageAsync(DistributedSearchPageV1 actual, DistributedSearchRf3Seed seed,
        long sourceUpperCut, long destinationUpperCut, long destinationEpoch)
    {
        await Assert.That(actual.Version).IsEqualTo(DistributedSearchRf3Seed.Version);
        await Assert.That(actual.Complete).IsTrue();
        await Assert.That(actual.Hits.IsDefault).IsFalse();
        await Assert.That(JsonDefaults.Serialize(actual.Hits).AsSpan().SequenceEqual(JsonDefaults.Serialize(Array.Empty<RankedDocument>()))).IsTrue();
        await WitnessesAsync(actual, seed, sourceUpperCut, destinationUpperCut, destinationEpoch);
    }

    private static async Task WitnessesAsync(DistributedSearchPageV1 actual, DistributedSearchRf3Seed seed,
        long sourceUpperCut, long destinationUpperCut, long destinationEpoch)
    {
        await Assert.That(actual.Leaves.Length).IsEqualTo(LeafCount);
        await LeafAsync(actual.Leaves[FirstLeaf], RemoteDocumentRf3Protocol.Partition,
            seed.DestinationVectorReceipt.Token.Position, destinationUpperCut, destinationEpoch);
        await LeafAsync(actual.Leaves[SecondLeaf], RemotePartitionQueryRf3Seed.Local,
            seed.SourceVectorReceipt.Token.Position, sourceUpperCut, DistributedSearchRf3Seed.InitialEpoch);
        await Assert.That(actual.StatisticsEpoch.Length).IsEqualTo(Sha256HexCharacters);
        await Assert.That(actual.StatisticsEpoch.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')).IsTrue();
        var names = System.Text.Json.JsonSerializer.SerializeToElement(actual, JsonDefaults.Options)
            .EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
        await Assert.That(string.Join(',', names)).IsEqualTo(SchemaNames);
    }

    private static RankedDocument[] Expected() =>
    [
        Hit(RemoteDocumentRf3Protocol.Reference, RemoteDocumentRf3Protocol.ProjectedJson,
            FirstRank, SecondRank),
        Hit(new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection,
            DistributedSearchRf3Seed.LongId), DistributedSearchRf3Seed.LongProjected, SecondRank, FirstRank),
        Hit(new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection,
            RemoteDocumentRf3Protocol.Document), RemotePartitionQueryRf3Seed.SourceProjected, ThirdRank, null)
    ];

    private static RankedDocument Hit(EntityRef reference, string json, int textRank, int? vectorRank)
    {
        var text = TextWeight / (FusionConstant + (double)textRank);
        var vector = vectorRank is { } rank ? VectorWeight / (FusionConstant + (double)rank) : 0d;
        SearchBranchContribution[] contributions = vectorRank is { } nativeRank
            ? [new(SearchBranchKind.Text, textRank, TextWeight, text),
                new(SearchBranchKind.Vector, nativeRank, VectorWeight, vector)]
            : [new(SearchBranchKind.Text, textRank, TextWeight, text)];
        return new(new(reference, DistributedSearchRf3Seed.Revision, json, true,
            [RemoteDocumentRf3Protocol.Secret]), text + vector,
            new(FusionConstant, [.. contributions]));
    }

    private static async Task LeafAsync(PartitionQueryLeafWitnessV1 actual, PartitionRef partition,
        long receiptCut, long upperCut, long epoch)
    {
        await Assert.That(actual.Partition).IsEqualTo(partition);
        await Assert.That(actual.CutPosition >= receiptCut && actual.CutPosition <= upperCut).IsTrue();
        await Assert.That(actual.PolicyEpoch).IsEqualTo(epoch);
        await Assert.That(actual.SchemaVersion).IsEqualTo(DistributedSearchRf3Seed.Revision);
        await Assert.That(actual.AccessPath).IsEqualTo(AccessPath);
    }
}
