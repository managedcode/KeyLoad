using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Independent full wait/result and bilingual canonical/native-page oracles around actual native failure and recovery.</summary>
internal static class SearchWaitAuthorityState
{
    internal const string ForeignAtomicPartition = "wrong-atomic-wait-authority";
    internal const string TextField = "/text";
    internal const long DifferentEpoch = 1;
    internal const long InvalidPosition = 0;
    private const string Root = "root";
    private const long OriginalSchemaVersion = 1;
    private const long OriginalPolicyEpoch = 1;

    internal static async Task DeniedAsync(TestDatabase database, SearchEngine search, WaitForIndexRequest request,
        ErrorCode code, string detail, CancellationToken cancellationToken)
    {
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        WaitForIndexResult? partial = null;
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
            partial = await search.WaitForIndexAsync(Root, request, cancellationToken)) ?? throw new InvalidOperationException();
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(failure.Message).IsEqualTo(detail);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task HealthyAsync(TestDatabase database, SearchEngine search, WaitForIndexRequest original,
        CancellationToken cancellationToken)
    {
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var actual = await search.WaitForIndexAsync(Root, original, cancellationToken);
        var expected = new WaitForIndexResult(original.MinimumToken, OriginalSchemaVersion, OriginalPolicyEpoch);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await NativeTextBilingualAudit.VerifyAsync(search, database.Partition, cancellationToken);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
