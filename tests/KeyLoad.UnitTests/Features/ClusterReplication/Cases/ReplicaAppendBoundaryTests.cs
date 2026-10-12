using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaAppendBoundaryTests
{
    [Test]
    public async Task OriginalValidateFailureLeavesBoundaryAndFullNativeCutUnchanged()
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var entry = fixture.Create("index-gap", 2);
        var before = ReplicaAppendBoundaryCut.Read(fixture);
        var boundary = new ReplicaSubmissionBoundary();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.Append([entry], boundary, default));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(boundary.IsStarted).IsFalse();
        await before.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await RequireHealthyAsync(fixture, entry with { Index = 1 });
    }

    [Test]
    public async Task OriginalNextStateCommittedReplacementLeavesFullNativeCutUnchanged()
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var original = fixture.Create("original-committed", 1);
        fixture.Log.Append([original]);
        fixture.Log.Commit(original.Index);
        var replacement = fixture.Create("different-command", original.Index);
        var before = ReplicaAppendBoundaryCut.Read(fixture);
        var boundary = new ReplicaSubmissionBoundary();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.Append([replacement], boundary, default));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(boundary.IsStarted).IsFalse();
        await before.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await RequireEntryAsync(fixture, original);
        await RequireHealthyAsync(fixture, fixture.Create("following", 2));
    }

    [Test]
    public async Task CancellationImmediatelyBeforeActualCommitDoesNotStartBoundary()
    {
        using var fixture = new ReplicaAppendBoundaryFixture();
        var entry = fixture.Create("canceled", 1);
        var before = ReplicaAppendBoundaryCut.Read(fixture);
        var boundary = new ReplicaSubmissionBoundary();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Log.Append([entry], boundary, cancellation.Token));
        await Assert.That(boundary.IsStarted).IsFalse();
        await before.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await RequireHealthyAsync(fixture, entry);
    }

    [Test]
    public async Task GenuineEntryAcknowledgedFaultStartsBoundaryAndRetainsNativeEntryAfterReopen()
    {
        var injected = Errors.Fail(ErrorCode.Conflict, "Original native EntryAcknowledged test fault.");
        var armed = true;
        using var fixture = new ReplicaAppendBoundaryFixture(stage =>
        {
            if (stage == ReplicaCrashBoundary.EntryAcknowledged && armed)
            { armed = false; throw injected; }
        });
        var entry = fixture.Create("actually-appended", 1);
        var before = ReplicaAppendBoundaryCut.Read(fixture);
        var state = fixture.Log.State;
        var boundary = new ReplicaSubmissionBoundary();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.Append([entry], boundary, default));
        await Assert.That(ReferenceEquals(failure, injected)).IsTrue();
        await Assert.That(boundary.IsStarted).IsTrue();
        var appended = ReplicaAppendBoundaryCut.Read(fixture);
        await before.RequireCanonicalAsync(appended);
        await Assert.That(appended.ReplicaPosition > before.ReplicaPosition).IsTrue();
        await ReplicaAppendBoundaryCut.RequireBytesAsync(NativeSerialization.Serialize(state with { LastIndex = entry.Index }),
            appended.HardState);
        await RequireEntryAsync(fixture, entry);
        fixture.Reopen();
        await appended.RequireAsync(ReplicaAppendBoundaryCut.Read(fixture));
        await RequireEntryAsync(fixture, entry);
        await RequireHealthyAsync(fixture, fixture.Create("healthy-after-fault", 2));
    }

    private static async Task RequireHealthyAsync(ReplicaAppendBoundaryFixture fixture, ReplicaEntry entry)
    {
        var boundary = new ReplicaSubmissionBoundary();
        fixture.Log.Append([entry], boundary, default);
        await Assert.That(boundary.IsStarted).IsTrue();
        await RequireEntryAsync(fixture, entry);
    }

    private static async Task RequireEntryAsync(ReplicaAppendBoundaryFixture fixture, ReplicaEntry original)
    {
        var actual = fixture.Log.ReadEntry(original.Index)
            ?? throw new InvalidOperationException("The original generated native entry is missing.");
        await ReplicaAppendBoundaryCut.RequireBytesAsync(NativeSerialization.Serialize(original), NativeSerialization.Serialize(actual));
    }
}
