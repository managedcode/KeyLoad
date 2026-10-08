namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class FollowerDocumentAssertions
{
    internal static async Task FullAsync(FollowerDocumentFixture fixture, FollowerDocumentReadResultV1 actual,
        long dataPosition, long authorityPosition, long revision, string? json, PrincipalRecord principal,
        bool redacted = false)
    {
        var data = new CommitToken(fixture.Owner.Incarnation, fixture.Db.Partition.AtomicPartitionId,
            dataPosition, fixture.Owner.PlacementEpoch);
        var authority = data with { Position = authorityPosition };
        var document = json is null ? null : new DocumentResult(fixture.Reference, revision, json,
            redacted, redacted ? ["/secret"] : []);
        var expected = new FollowerDocumentReadResultV1(FollowerDocumentFixture.Version,
            DocumentFollowerReadMode.FollowerCommittedSnapshot, fixture.ReplicaId, FollowerDocumentFixture.Term,
            FollowerDocumentFixture.Term, data, authority, authorityPosition - dataPosition, principal.PolicyEpoch, document);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task FailureUnchangedAsync(FollowerDocumentFixture fixture, Action operation,
        ErrorCode code, string? detail = null)
    {
        var before = fixture.State();
        var position = fixture.Db.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(error.Code).IsEqualTo(code);
        if (detail is not null)
        { await Assert.That(error.Message).IsEqualTo(detail); }
        await Assert.That(fixture.State().SequenceEqual(before)).IsTrue();
        await Assert.That(fixture.Db.Store.Position).IsEqualTo(position);
    }

    internal static async Task CurrentAsync(FollowerDocumentFixture fixture, long revision, string json)
    {
        var actual = fixture.Db.Database.GetDocument(FollowerDocumentFixture.Root, fixture.Reference);
        var expected = new DocumentResult(fixture.Reference, revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
