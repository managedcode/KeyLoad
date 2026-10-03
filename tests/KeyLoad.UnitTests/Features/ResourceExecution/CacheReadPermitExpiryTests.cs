using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheReadPermitExpiryTests
{
    private static readonly TimeSpan Margin = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan GrantPreparationDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(30);

    [Test]
    public async Task PreparedAgeRejectsAtTenSecondsWithoutConsumingSequence()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        using var permit = new CacheReadPermit(TimeProvider.System);
        var grant = Guid.NewGuid();
        var stalePrepared = TimeProvider.System.GetTimestamp();
        await Task.Delay(CacheReadPermitLimits.PrepareValidity + Margin, TimeProvider.System, linked.Token);

        await Assert.That(permit.TryAccept(grant, 1, stalePrepared, out _)).IsFalse();
        await Assert.That(permit.TryCapture(out _)).IsFalse();

        var prepared = TimeProvider.System.GetTimestamp();
        await Assert.That(permit.TryAccept(grant, 1, prepared, out var receipt)).IsTrue();
        await Assert.That(receipt).IsEqualTo(new CacheReadPermitAcceptance(1, 0, false));
    }

    [Test]
    public async Task LeaseExpiresAtFifteenSecondsFromItsPrepareTimestamp()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        using var permit = new CacheReadPermit(TimeProvider.System);
        var clock = TimeProvider.System;
        var prepared = clock.GetTimestamp();
        await Task.Delay(GrantPreparationDelay, clock, linked.Token);
        var acceptStartedAt = clock.GetTimestamp();
        var accepted = permit.TryAccept(Guid.NewGuid(), 1, prepared, out _);
        await Assert.That(accepted).IsTrue();
        await Assert.That(permit.TryCapture(out var revision)).IsTrue();
        await Assert.That(revision).IsEqualTo(1);

        var ageAtReceipt = clock.GetElapsedTime(prepared, acceptStartedAt);
        await Assert.That(ageAtReceipt >= GrantPreparationDelay).IsTrue();
        await Assert.That(ageAtReceipt < CacheReadPermitLimits.PrepareValidity).IsTrue();
        var currentPrepareAge = clock.GetElapsedTime(prepared, clock.GetTimestamp());
        var remaining = CacheReadPermitLimits.LeaseValidity + Margin - currentPrepareAge;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, clock, linked.Token);
        }

        var isCurrent = permit.IsCurrent(revision);
        var captured = permit.TryCapture(out var expiredRevision);
        var completedAt = clock.GetTimestamp();
        var prepareAge = clock.GetElapsedTime(prepared, completedAt);
        var receiptAge = clock.GetElapsedTime(acceptStartedAt, completedAt);
        await Assert.That(prepareAge >= CacheReadPermitLimits.LeaseValidity).IsTrue();
        await Assert.That(receiptAge < CacheReadPermitLimits.LeaseValidity).IsTrue();
        await Assert.That(isCurrent).IsFalse();
        await Assert.That(captured).IsFalse();
        await Assert.That(expiredRevision).IsEqualTo(0);

        var renewed = permit.TryAccept(Guid.NewGuid(), 2, clock.GetTimestamp(), out var receipt);
        await Assert.That(renewed).IsTrue();
        await Assert.That(receipt).IsEqualTo(new CacheReadPermitAcceptance(2, 1, false));
        await Assert.That(permit.IsCurrent(revision)).IsFalse();
    }
}
