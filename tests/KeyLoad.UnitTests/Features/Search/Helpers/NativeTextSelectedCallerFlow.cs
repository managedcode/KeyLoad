using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextSelectedCallerFlow
{
    internal const string Mismatch = "The native text projection does not match the authorized source cut.";
    private const string Reader = "selected-text-reader";
    private const string Root = "root";
    private const long OriginalPolicy = 1;
    private const long RevokedPolicy = 2;
    private const long MissingGeneration = 2;
    private const long Revision = 1;
    private const double Score = 1d / 61d;

    internal static Task PersistAsync(TestDatabase database, CancellationToken token)
        => PersistAsync(database, revoked: false, OriginalPolicy, token);

    private static async Task PersistAsync(TestDatabase database, bool revoked, long epoch, CancellationToken token)
    {
        var principal = new PrincipalRecord(Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, NativeTextBilingualAudit.Collection, Capability.Query | Capability.DocumentsRead)], [])
        { Revoked = revoked, PolicyEpoch = epoch };
        _ = await NativeTextMaintenanceCommit.ExecuteAsync<PrincipalRecord>(database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal), Guid.NewGuid(), token);
    }

    internal static async Task VerifyAsync(TestDatabase database, SearchEngine search,
        TextIndexSelectionV1 selection, CancellationToken token)
    {
        var request = NativeTextBilingualAudit.Request(database.Partition, NativeTextBilingualAudit.EnglishQuery)
            with
        { TextIndex = selection };
        RankedDocument[] expected = [new(new(new(database.Partition, NativeTextBilingualAudit.Collection,
            NativeTextBilingualAudit.EnglishId), Revision, NativeTextBilingualAudit.EnglishJson, false, []), Score)];
        await Assert.That(JsonDefaults.Serialize(await search.SearchAsync(Reader, request, token)).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await PersistAsync(database, revoked: true, RevokedPolicy, token);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var cut = database.Store.Position;
        RankedDocument[]? partial = null;
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
            await search.SearchAsync(Reader, request with { TextIndex = selection with { Generation = MissingGeneration } }, token))
            ?? throw new InvalidOperationException();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await Assert.That(JsonDefaults.Serialize(await search.SearchAsync(Root, request with { TextIndex = null }, token)).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
