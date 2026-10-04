using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheReadPermitAcceptanceTests
{
    [Test]
    public async Task CurrentAcceptanceRequiresEveryReceiptFieldAndRejectsColdOrClosedState()
    {
        using var permit = new CacheReadPermit(TimeProvider.System);
        await Assert.That(permit.IsCurrentAcceptance(default)).IsFalse();
        var firstGrant = Guid.NewGuid();
        var firstPrepared = TimeProvider.System.GetTimestamp();
        await Assert.That(permit.TryAccept(firstGrant, 1, firstPrepared, out var first)).IsTrue();

        await Assert.That(first).IsEqualTo(new CacheReadPermitAcceptance(1, 0, false));
        await Assert.That(permit.IsCurrentAcceptance(first)).IsTrue();
        await Assert.That(permit.IsCurrentAcceptance(first with { Revision = 2 })).IsFalse();
        await Assert.That(permit.IsCurrentAcceptance(first with { PreviousRevision = 1 })).IsFalse();
        await Assert.That(permit.IsCurrentAcceptance(first with { Continuous = true })).IsFalse();

        var secondGrant = Guid.NewGuid();
        await Assert.That(permit.TryAccept(secondGrant, 2, TimeProvider.System.GetTimestamp(), out var second)).IsTrue();
        await Assert.That(second).IsEqualTo(new CacheReadPermitAcceptance(2, 1, true));
        await Assert.That(permit.IsCurrentAcceptance(first)).IsFalse();
        await Assert.That(permit.IsCurrentAcceptance(second)).IsTrue();
        await Assert.That(permit.TryWithdraw(firstGrant, first.Revision)).IsFalse();
        await Assert.That(permit.IsCurrentAcceptance(second)).IsTrue();
        await Assert.That(permit.IsCurrentAcceptance(second with { PreviousRevision = 0 })).IsFalse();
        await Assert.That(permit.IsCurrentAcceptance(second with { Continuous = false })).IsFalse();
        await Assert.That(permit.TryWithdraw(secondGrant, second.Revision)).IsTrue();
        await Assert.That(permit.IsCurrentAcceptance(second)).IsFalse();

        permit.Dispose();
        await Assert.That(permit.IsCurrentAcceptance(second)).IsFalse();
        await Assert.That(permit.IsCurrentAcceptance(default)).IsFalse();
    }

    [Test]
    [NotInParallel]
    public async Task AcceptedReceiptExpiresFromItsRealPrepareTimestamp()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        using var permit = new CacheReadPermit(TimeProvider.System);
        var clock = TimeProvider.System;
        var prepared = clock.GetTimestamp();
        await Task.Delay(TimeSpan.FromSeconds(2), clock, linked.Token);
        var beforeAccept = clock.GetTimestamp();
        await Assert.That(permit.TryAccept(Guid.NewGuid(), 1, prepared, out var receipt)).IsTrue();
        await Assert.That(permit.IsCurrentAcceptance(receipt)).IsTrue();

        var acceptedPrepareAge = clock.GetElapsedTime(prepared, beforeAccept);
        await Assert.That(acceptedPrepareAge >= TimeSpan.FromSeconds(2)).IsTrue();
        await Assert.That(acceptedPrepareAge < CacheReadPermitLimits.PrepareValidity).IsTrue();
        var currentPrepareAge = clock.GetElapsedTime(prepared, clock.GetTimestamp());
        var remaining = CacheReadPermitLimits.LeaseValidity + TimeSpan.FromMilliseconds(50) - currentPrepareAge;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, clock, linked.Token);
        }

        var current = permit.IsCurrentAcceptance(receipt);
        var captured = permit.TryCapture(out var expiredRevision);
        var completion = clock.GetTimestamp();
        var prepareAge = clock.GetElapsedTime(prepared, completion);
        var observationAge = clock.GetElapsedTime(beforeAccept, completion);
        await Assert.That(prepareAge >= CacheReadPermitLimits.LeaseValidity).IsTrue();
        await Assert.That(observationAge < CacheReadPermitLimits.LeaseValidity).IsTrue();
        await Assert.That(current).IsFalse();
        await Assert.That(captured).IsFalse();
        await Assert.That(expiredRevision).IsEqualTo(0);
        await Assert.That(permit.TryAccept(Guid.NewGuid(), 2, clock.GetTimestamp(), out var renewal)).IsTrue();
        await Assert.That(renewal).IsEqualTo(new CacheReadPermitAcceptance(2, 1, false));
        await Assert.That(permit.IsCurrent(receipt.Revision)).IsFalse();
    }
}
