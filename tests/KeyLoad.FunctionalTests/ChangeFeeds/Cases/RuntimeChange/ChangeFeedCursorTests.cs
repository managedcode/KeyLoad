using System.Buffers.Text;
using KeyLoad.Core.InternalSerialization;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

/// <summary>AC-IS-001: actual persisted change-feed cursors use the owning execution claims codec.</summary>
internal sealed class ChangeFeedCursorTests
{
    private const string Root = "root";
    private const string Collection = "orders";
    private const string First = "a";
    private const string Second = "b";
    private const string Json = "{}";
    private const char SignatureSeparator = '.';
    private const int SignatureBytes = 32;
    private const int One = 1;

    [Test]
    public async Task ClaimsContinueRealChangesAndTamperingCannotReplaceTheCursor()
    {
        using var fixture = new TestDatabase();
        fixture.Configure(Collection, ResourceKind.Collection);
        fixture.Commit(new PutDocument(Collection, First, Json), new PutDocument(Collection, Second, Json));
        var first = fixture.Database.ReadChangeFeed(Root, new(fixture.Partition, Collection, Limit: One));
        var claims = fixture.Database.Verify<ChangeFeedClaims>(first.Cursor);
        var encoded = OrleansSerialization.Serialize(claims);
        await Assert.That(OrleansSerialization.Deserialize<ChangeFeedClaims>(encoded)).IsEqualTo(claims);
        await Assert.That(OrleansSerialization.Measure(claims)).IsEqualTo((long)encoded.Length);
        await Assert.That(first.Cursor.StartsWith(CoreClaims.Prefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(first.Changes.Single().Reference.Id).IsEqualTo(First);
        await Assert.That(claims.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(claims.Partition).IsEqualTo(fixture.Partition);
        await Assert.That(claims.Collection).IsEqualTo(Collection);
        await Assert.That(claims.PrincipalId).IsEqualTo(Root);
        await Assert.That(claims.After).IsEqualTo(first.ThroughSequence);
        var tampered = first.Cursor[..(first.Cursor.LastIndexOf(SignatureSeparator) + One)]
            + Base64Url.EncodeToString(new byte[SignatureBytes]);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.ReadChangeFeed(Root,
            new(fixture.Partition, Collection, tampered, Limit: One))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        var second = fixture.Database.ReadChangeFeed(Root, new(fixture.Partition, Collection, first.Cursor, Limit: One));
        await Assert.That(second.Changes.Single().Reference.Id).IsEqualTo(Second);
        await Assert.That(second.HasMore).IsFalse();
        await Assert.That(second.ThroughSequence).IsEqualTo(first.Tail);
    }
}
