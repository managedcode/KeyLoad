using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3Oracle
{
    private const string AccessPath = "distributedCanonicalSearchV1";
    internal static async Task PageAsync(DistributedSearchPageV1 actual, GlobalBranchLayoutRf3State state,
        long epoch, long[][]? nativeCuts)
    {
        await Assert.That(actual.Version).IsEqualTo(GlobalBranchLayoutRf3State.Version);
        await Assert.That(actual.Complete).IsTrue();
        await SqlRf3Protocol.EqualAsync(Expected(), actual.Hits.ToArray());
        await Assert.That(actual.Leaves.Length).IsEqualTo(2);
        var destination = actual.Leaves[0];
        var original = actual.Leaves[1];
        await LeafAsync(destination, RemoteDocumentRf3Protocol.Partition, epoch, nativeCuts?[1]);
        await LeafAsync(original, RemotePartitionQueryRf3Seed.Local, epoch, nativeCuts?[state.Moved is null ? 0 : 1]);
        await Assert.That(actual.StatisticsEpoch.Length).IsEqualTo(64);
        await Assert.That(actual.StatisticsEpoch.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f')).IsTrue();
        var names = System.Text.Json.JsonSerializer.SerializeToElement(actual, JsonDefaults.Options)
            .EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
        await Assert.That(string.Join(',', names)).IsEqualTo("complete,hits,leaves,statisticsEpoch,version");
    }
    private static async Task LeafAsync(PartitionQueryLeafWitnessV1 actual, PartitionRef partition, long epoch, long[]? nativeCuts)
    {
        await Assert.That(actual.Partition).IsEqualTo(partition);
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
        if (nativeCuts is not null)
        {
            await Assert.That(nativeCuts.Contains(actual.CutPosition)).IsTrue();
        }
        await Assert.That(actual.PolicyEpoch).IsEqualTo(epoch);
        await Assert.That(actual.SchemaVersion).IsEqualTo(GlobalBranchLayoutRf3State.Revision);
        await Assert.That(actual.AccessPath).IsEqualTo(AccessPath);
    }
    private static RankedDocument[] Expected() =>
    [
        Hit(RemoteDocumentRf3Protocol.Reference, RemoteDocumentRf3Protocol.ProjectedJson, 1, 2),
        Hit(new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection,
            DistributedSearchRf3Seed.LongId), DistributedSearchRf3Seed.LongProjected, 2, 1),
        Hit(new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection,
            RemoteDocumentRf3Protocol.Document), RemotePartitionQueryRf3Seed.SourceProjected, 3, null)
    ];
    private static RankedDocument Hit(EntityRef reference, string body, int textRank, int? vectorRank)
    {
        var text = 2d / (GlobalBranchLayoutRf3State.Fusion + (double)textRank);
        var vector = vectorRank is { } rank ? 1d / (GlobalBranchLayoutRf3State.Fusion + (double)rank) : 0d;
        SearchBranchContribution[] contributions = vectorRank is { } present
            ? [new(SearchBranchKind.Text, textRank, 2d, text), new(SearchBranchKind.Vector, present, 1d, vector)]
            : [new(SearchBranchKind.Text, textRank, 2d, text)];
        return new(new(reference, 1L, body, true, [RemoteDocumentRf3Protocol.Secret]), text + vector,
            new(GlobalBranchLayoutRf3State.Fusion, [.. contributions]));
    }
}
