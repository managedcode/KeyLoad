using System.Text.Json;
using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentSessionReadTests
{
    private const string Collection = "session-documents";
    private const string DocumentId = "one";
    private const string Root = "root";
    private const string Reader = "reader";
    private const string FirstJson = "{\"value\":\"first\"}";
    private const string SecondJson = "{\"value\":\"second\"}";
    private const long First = 1;
    private const long Second = 2;
    private const long Invalid = 0;
    private const int SnapshotBound = 128;

    [Test]
    public async Task Kl021MinimumTokenRejectsInvalidAuthorityWithoutEffectsAndHealthyReadContinues()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var receipt = Apply(db, FirstJson, Invalid, First);
        var reference = new EntityRef(db.Partition, Collection, DocumentId);
        var request = new GetDocumentRequest(reference, receipt.Token);
        var native = NativeSerialization.Deserialize<GetDocumentRequest>(NativeSerialization.Serialize(request));
        var json = JsonSerializer.Deserialize<GetDocumentRequest>(JsonSerializer.Serialize(request, JsonDefaults.Options), JsonDefaults.Options)!;
        await LiteralAsync(db.Database.GetDocument(Root, native.Reference, native.MinimumToken), reference, FirstJson, First);
        await LiteralAsync(db.Database.GetDocument(Root, json.Reference, json.MinimumToken), reference, FirstJson, First);
        var before = Snapshot(db);
        var position = db.Store.Position;
        CommitToken[] rejected =
        [
            receipt.Token with { Incarnation = Guid.NewGuid() },
            receipt.Token with { AtomicPartitionId = Guid.NewGuid().ToString() },
            receipt.Token with { OwnershipEpoch = receipt.Token.OwnershipEpoch + First },
            receipt.Token with { Position = Invalid },
            receipt.Token with { Position = Second }
        ];
        foreach (var token in rejected)
        {
            var direct = new GetDocumentRequest(reference, token);
            GetDocumentRequest[] wireRequests =
            [
                direct,
                NativeSerialization.Deserialize<GetDocumentRequest>(NativeSerialization.Serialize(direct)),
                JsonSerializer.Deserialize<GetDocumentRequest>(JsonSerializer.Serialize(direct, JsonDefaults.Options), JsonDefaults.Options)!
            ];
            foreach (var rejectedRequest in wireRequests)
            {
                var error = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.GetDocument(Root,
                    rejectedRequest.Reference, rejectedRequest.MinimumToken));
                await Assert.That(error.Code).IsEqualTo(ErrorCode.TokenInvalidated);
                await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
                await Assert.That(db.Store.Position).IsEqualTo(position);
                await LiteralAsync(db.Database.GetDocument(Root, reference, receipt.Token), reference, FirstJson, First);
            }
        }
        var next = Apply(db, SecondJson, First, Second);
        await LiteralAsync(db.Database.GetDocument(Root, reference, receipt.Token), reference, SecondJson, Second);
        await LiteralAsync(db.Database.GetDocument(Root, reference, next.Token), reference, SecondJson, Second);
    }

    [Test]
    public async Task Kl021CancelledMinimumReadReturnsNoResultAndFreshNativeReadSucceeds()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var receipt = Apply(db, FirstJson, Invalid, First);
        var reference = new EntityRef(db.Partition, Collection, DocumentId);
        var before = Snapshot(db);
        var position = db.Store.Position;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var error = Assert.ThrowsExactly<OperationCanceledException>(() =>
            db.Database.GetDocument(Root, reference, receipt.Token, cancelled.Token));
        await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await LiteralAsync(db.Database.GetDocument(Root, reference, receipt.Token), reference, FirstJson, First);
    }

    [Test]
    public async Task Kl021MinimumTokenUsesFreshPersistedAuthorizationAndHealthyGrantRestoration()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var receipt = Apply(db, FirstJson, Invalid, First);
        var reference = new EntityRef(db.Partition, Collection, DocumentId);
        var principal = new PrincipalRecord(Reader, db.Partition.TenantId,
            [new(db.Partition.DatabaseId, Collection, Capability.DocumentsRead)], []);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await LiteralAsync(db.Database.GetDocument(Reader, reference, receipt.Token), reference, FirstJson, First);
        var revoked = principal with { Grants = [], PolicyEpoch = principal.PolicyEpoch + First };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked)).Get<PrincipalRecord>();
        var before = Snapshot(db);
        var position = db.Store.Position;
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.GetDocument(Reader, reference,
            receipt.Token with { Incarnation = Guid.NewGuid() }));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal with
        { PolicyEpoch = revoked.PolicyEpoch + First })).Get<PrincipalRecord>();
        await LiteralAsync(db.Database.GetDocument(Reader, reference, receipt.Token), reference, FirstJson, First);
    }

    [Test]
    public async Task Kl021MissingAppliedAuthorityFailsClosedAndRestoredNativeReadContinues()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var receipt = Apply(db, FirstJson, Invalid, First);
        var reference = new EntityRef(db.Partition, Collection, DocumentId);
        var original = db.Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes))
            ?? throw new InvalidOperationException("The native applied seed is missing.");
        db.Store.Commit((transaction, _) => { transaction.Delete(KeySpace.AppliedBytes); return true; });
        var before = Snapshot(db);
        var position = db.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.GetDocument(Root, reference, receipt.Token));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo("The canonical applied position is invalid.");
        await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        db.Store.Commit((transaction, _) => { transaction.Put(KeySpace.AppliedBytes, original); return true; });
        await LiteralAsync(db.Database.GetDocument(Root, reference, receipt.Token), reference, FirstJson, First);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Kl021CorruptAppliedAuthorityFailsClosedWithoutEffectsAndRestoredReadContinues(bool truncated)
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var receipt = Apply(db, FirstJson, Invalid, First);
        var reference = new EntityRef(db.Partition, Collection, DocumentId);
        var original = db.Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes))
            ?? throw new InvalidOperationException("The native applied seed is missing.");
        var corrupt = truncated ? original[..^1] : NativeSerialization.Serialize(-First);
        db.Store.Commit((transaction, _) => { transaction.Put(KeySpace.AppliedBytes, corrupt); return true; });
        var before = Snapshot(db);
        var position = db.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.GetDocument(Root, reference, receipt.Token));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(truncated
            ? "The internal binary payload is invalid." : "The canonical applied position is invalid.");
        await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        db.Store.Commit((transaction, _) => { transaction.Put(KeySpace.AppliedBytes, original); return true; });
        await LiteralAsync(db.Database.GetDocument(Root, reference, receipt.Token), reference, FirstJson, First);
        var next = Apply(db, SecondJson, First, Second);
        await LiteralAsync(db.Database.GetDocument(Root, reference, receipt.Token), reference, SecondJson, Second);
        await LiteralAsync(db.Database.GetDocument(Root, reference, next.Token), reference, SecondJson, Second);
    }

    private static CommitReceipt Apply(TestDatabase db, string json, long revision, long applied)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, db.Partition, [new PutDocument(Collection, DocumentId, json, revision, ExplicitReplacement: revision > Invalid)]);
        return db.Database.Apply(new(id, OperationKind.Batch, Root, db.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(command, JsonDefaults.Options)), applied).Get<CommitReceipt>();
    }

    private static (string Key, string Value)[] Snapshot(TestDatabase db) => db.Store.Read(view =>
    {
        var page = view.Scan([], SnapshotBound);
        if (page.HasMore)
        { throw new InvalidOperationException("The complete session-read fixture exceeds its native snapshot bound."); }
        return page.Records.Select(record => (Convert.ToHexString(record.Key.Span),
            Convert.ToHexString(record.Value.Span))).ToArray();
    });

    private static async Task LiteralAsync(DocumentResult? actual, EntityRef reference, string json, long revision)
    {
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Reference).IsEqualTo(reference);
        await Assert.That(actual.Json).IsEqualTo(json);
        await Assert.That(actual.Revision).IsEqualTo(revision);
        await Assert.That(actual.Redacted).IsFalse();
        await Assert.That(actual.RedactedFields).IsEmpty();
    }
}
