using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheReadPermitTests
{
    [Test]
    public async Task StartsColdAndAcceptsFreshReceiverPreparedLease()
    {
        using var permit = new CacheReadPermit(TimeProvider.System);
        await Assert.That(permit.TryCapture(out var coldRevision)).IsFalse();
        await Assert.That(coldRevision).IsEqualTo(0);

        var grant = Guid.NewGuid();
        var prepared = TimeProvider.System.GetTimestamp();
        var accepted = permit.TryAccept(grant, 1, prepared, out var receipt);

        await Assert.That(accepted).IsTrue();
        await Assert.That(receipt).IsEqualTo(new CacheReadPermitAcceptance(1, 0, false));
        await Assert.That(permit.TryCapture(out var revision)).IsTrue();
        await Assert.That(revision).IsEqualTo(1);
        await Assert.That(permit.IsCurrent(revision)).IsTrue();
    }

    [Test]
    public async Task RejectedInputsDoNotChangeCurrentLeaseOrConsumeSequence()
    {
        using var permit = new CacheReadPermit(TimeProvider.System);
        var grant = Guid.NewGuid();
        var prepared = TimeProvider.System.GetTimestamp();
        await Assert.That(permit.TryAccept(grant, 5, prepared, out _)).IsTrue();

        await AssertRejectedAsync(permit, Guid.Empty, 6, prepared, 5);
        await AssertRejectedAsync(permit, Guid.NewGuid(), 0, prepared, 5);
        await AssertRejectedAsync(permit, Guid.NewGuid(), -1, prepared, 5);
        await AssertRejectedAsync(permit, Guid.NewGuid(), 5, prepared, 5);
        await AssertRejectedAsync(permit, Guid.NewGuid(), 4, prepared, 5);
        var future = checked(TimeProvider.System.GetTimestamp() + TimeProvider.System.TimestampFrequency);
        await AssertRejectedAsync(permit, Guid.NewGuid(), 6, future, 5);
        await AssertRejectedAsync(permit, Guid.NewGuid(), 6, long.MinValue, 5);

        await Assert.That(permit.IsCurrent(5)).IsTrue();
        await Assert.That(permit.TryAccept(Guid.NewGuid(), 6, TimeProvider.System.GetTimestamp(), out _)).IsTrue();
    }

    [Test]
    public async Task RenewalReportsContinuityAndInvalidatesPreviousCapturedRevision()
    {
        using var permit = new CacheReadPermit(TimeProvider.System);
        var firstGrant = Guid.NewGuid();
        var firstPrepared = TimeProvider.System.GetTimestamp();
        await Assert.That(permit.TryAccept(firstGrant, 1, firstPrepared, out _)).IsTrue();
        await Assert.That(permit.TryCapture(out var firstRevision)).IsTrue();

        var renewal = permit.TryAccept(Guid.NewGuid(), 2, TimeProvider.System.GetTimestamp(), out var receipt);

        await Assert.That(renewal).IsTrue();
        await Assert.That(receipt).IsEqualTo(new CacheReadPermitAcceptance(2, 1, true));
        await Assert.That(permit.IsCurrent(firstRevision)).IsFalse();
        await Assert.That(permit.TryCapture(out var currentRevision)).IsTrue();
        await Assert.That(currentRevision).IsEqualTo(2);
        await Assert.That(permit.IsCurrent(currentRevision)).IsTrue();
    }

    [Test]
    public async Task WithdrawalRequiresExactGrantAndRevisionAndPreservesSequenceHistory()
    {
        using var permit = new CacheReadPermit(TimeProvider.System);
        var grant = Guid.NewGuid();
        await Assert.That(permit.TryAccept(grant, 7, TimeProvider.System.GetTimestamp(), out _)).IsTrue();

        await Assert.That(permit.TryWithdraw(Guid.NewGuid(), 7)).IsFalse();
        await Assert.That(permit.TryWithdraw(grant, 6)).IsFalse();
        await Assert.That(permit.IsCurrent(7)).IsTrue();
        await Assert.That(permit.TryWithdraw(grant, 7)).IsTrue();
        await Assert.That(permit.TryCapture(out _)).IsFalse();
        await Assert.That(permit.TryWithdraw(grant, 7)).IsFalse();
        await Assert.That(permit.TryAccept(Guid.NewGuid(), 7, TimeProvider.System.GetTimestamp(), out _)).IsFalse();
        await Assert.That(permit.TryCapture(out _)).IsFalse();

        var accepted = permit.TryAccept(Guid.NewGuid(), 8, TimeProvider.System.GetTimestamp(), out var receipt);
        await Assert.That(accepted).IsTrue();
        await Assert.That(receipt).IsEqualTo(new CacheReadPermitAcceptance(8, 7, false));
        await Assert.That(permit.TryWithdraw(grant, 7)).IsFalse();
        await Assert.That(permit.IsCurrent(8)).IsTrue();
    }

    [Test]
    public async Task FinalSequenceCanBeAcceptedButNeverWraps()
    {
        using var permit = new CacheReadPermit(TimeProvider.System);
        var accepted = permit.TryAccept(Guid.NewGuid(), long.MaxValue, TimeProvider.System.GetTimestamp(), out var receipt);

        await Assert.That(accepted).IsTrue();
        await Assert.That(receipt.Revision).IsEqualTo(long.MaxValue);
        await AssertRejectedAsync(permit, Guid.NewGuid(), 1, TimeProvider.System.GetTimestamp(), long.MaxValue);
        await Assert.That(permit.IsCurrent(long.MaxValue)).IsTrue();
    }

    [Test]
    public async Task DisposePublishesColdAndPermanentlyRejectsAcceptance()
    {
        using var permit = new CacheReadPermit(TimeProvider.System);
        var grant = Guid.NewGuid();
        await Assert.That(permit.TryAccept(grant, 1, TimeProvider.System.GetTimestamp(), out _)).IsTrue();
        permit.Dispose();
        permit.Dispose();

        await Assert.That(permit.TryCapture(out var revision)).IsFalse();
        await Assert.That(revision).IsEqualTo(0);
        await Assert.That(permit.IsCurrent(1)).IsFalse();
        await Assert.That(permit.TryAccept(Guid.NewGuid(), 2, TimeProvider.System.GetTimestamp(), out _)).IsFalse();
        await Assert.That(permit.TryWithdraw(grant, 1)).IsFalse();
    }

    private static async Task AssertRejectedAsync(CacheReadPermit permit, Guid grant, long sequence, long prepared,
        long expectedRevision)
    {
        await Assert.That(permit.TryAccept(grant, sequence, prepared, out _)).IsFalse();
        await Assert.That(permit.TryCapture(out var revision)).IsTrue();
        await Assert.That(revision).IsEqualTo(expectedRevision);
    }
}
