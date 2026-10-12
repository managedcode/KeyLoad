using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

internal sealed class NativeSubmissionJournalReadTests
{
    [Test]
    public async Task CompleteOriginalNativeEntryAndStateHaveNoReadEffectsAndHealthyContinuation()
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var original = Prepare(fixture);
        var before = ReplicaAppendBoundaryCut.Read(fixture);
        var proof = Read(fixture, original);
        await Assert.That(proof.NodeId).IsEqualTo(fixture.Store.Identity.NodeId);
        await Assert.That(proof.Incarnation).IsEqualTo(fixture.Configuration.Incarnation);
        await Assert.That(proof.Position).IsEqualTo(fixture.Store.Position);
        await ReplicaAppendBoundaryCut.RequireBytesAsync(ReplicaProtocolCodec.Serialize(fixture.Log.State), proof.NativeHardState);
        await ReplicaAppendBoundaryCut.RequireBytesAsync(ReplicaProtocolCodec.Serialize(original), proof.NativeEntry);
        await ReplicaAppendBoundaryCut.RequireBytesAsync(NativeSerialization.Serialize(original), NativeSerialization.Serialize(proof.Entry));
        await before.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await HealthyAsync(fixture);
    }

    [Test]
    public async Task WrongObservedTermAndPersistedMembershipFailWithoutEffectsAndHealthyContinuation()
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var original = Prepare(fixture);
        var before = ReplicaAppendBoundaryCut.Read(fixture);
        var wrongTerm = Assert.ThrowsExactly<KeyLoadException>(() => NativeSubmissionJournalRead.Run(fixture.Store,
            fixture.Canonical.Database, fixture.Configuration, original.Index, checked(original.Term + 1), default));
        await Assert.That(wrongTerm.Code).IsEqualTo(ErrorCode.Corruption);
        var foreign = fixture.Configuration with
        { VoterIds = [fixture.Configuration.LocalId, "foreign-journal-b", "foreign-journal-c"] };
        var wrongMembership = Assert.ThrowsExactly<KeyLoadException>(() => NativeSubmissionJournalRead.Run(fixture.Store,
            fixture.Canonical.Database, foreign, original.Index, original.Term, default));
        await Assert.That(wrongMembership.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => NativeSubmissionJournalRead.Run(fixture.Store,
            fixture.Canonical.Database, fixture.Configuration, original.Index, original.Term, canceled.Token));
        await before.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        _ = Read(fixture, original);
        await HealthyAsync(fixture);
    }

    [Test]
    public async Task ActualOversizeNativeValueFailsBeforeDecodeWithoutReadEffectsAndRecovers()
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var original = Prepare(fixture);
        var key = ReplicaProtocol.EntryStorageKey(original.Index);
        var originalBytes = ReplicaProtocolCodec.Serialize(original);
        fixture.Store.Commit((tx, _) =>
        { tx.Put(key, new byte[NativeSubmissionJournalReadBudget.MaximumRecordBytes + 1]); return true; });
        var oversized = ReplicaAppendBoundaryCut.Read(fixture);
        Assert.ThrowsExactly<InvalidDataException>(() => Read(fixture, original));
        await oversized.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        fixture.Store.Commit((tx, _) => { tx.Put(key, originalBytes); return true; });
        var restored = Read(fixture, original);
        await ReplicaAppendBoundaryCut.RequireBytesAsync(originalBytes, restored.NativeEntry);
        await HealthyAsync(fixture);
    }

    [Test]
    public async Task ActualOwnerReadBudgetRefusalPreservesOriginalFailureAndNativeCutAndFollowingRead()
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var original = Prepare(fixture);
        var before = ReplicaAppendBoundaryCut.Read(fixture);
        var failure = new InvalidDataException("The actual shared original-owner read budget is exhausted.");
        long observed = 0;
        var refused = Assert.ThrowsExactly<InvalidDataException>(() => NativeSubmissionJournalRead.Run(fixture.Store,
            fixture.Canonical.Database, fixture.Configuration, original.Index, original.Term, default,
            bytes => { observed = checked(observed + bytes); throw failure; }));
        await Assert.That(ReferenceEquals(failure, refused)).IsTrue();
        await Assert.That(observed > 0).IsTrue();
        await before.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await ReplicaAppendBoundaryCut.RequireBytesAsync(ReplicaProtocolCodec.Serialize(original),
            Read(fixture, original).NativeEntry);
        await HealthyAsync(fixture);
    }

    private static ReplicaEntry Prepare(ReplicaAppendBoundaryFixture fixture)
    {
        var original = fixture.Create("journal-original", 1);
        fixture.Log.Append([original]);
        fixture.Log.Commit(original.Index);
        return original;
    }

    private static NativeSubmissionJournalCut Read(ReplicaAppendBoundaryFixture fixture, ReplicaEntry original)
        => NativeSubmissionJournalRead.Run(fixture.Store, fixture.Canonical.Database, fixture.Configuration,
            original.Index, original.Term, default);

    private static async Task HealthyAsync(ReplicaAppendBoundaryFixture fixture)
    {
        var following = fixture.Create("journal-following", checked(fixture.Log.State.LastIndex + 1));
        fixture.Log.Append([following]);
        fixture.Log.Commit(following.Index);
        var proof = Read(fixture, following);
        await ReplicaAppendBoundaryCut.RequireBytesAsync(ReplicaProtocolCodec.Serialize(following), proof.NativeEntry);
    }
}
