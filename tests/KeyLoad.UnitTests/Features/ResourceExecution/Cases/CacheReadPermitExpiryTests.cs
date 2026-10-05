using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheReadPermitExpiryTests
{
    private static readonly TimeSpan Margin = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan GrantPreparationDelay = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinimumWait = TimeSpan.FromMilliseconds(1);

    [Test]
    public async Task PreparedAgeRejectsAtTenSecondsWithoutConsumingSequence()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        using var permit = new CacheReadPermit(TimeProvider.System, UnitAdmissionOptions.Permit());
        var grant = Guid.NewGuid();
        var stalePrepared = TimeProvider.System.GetTimestamp();
        await WaitForPreparedAgeAsync(TimeProvider.System, stalePrepared,
            CacheReadPermitLimits.PrepareValidity + Margin, linked.Token);

        await Assert.That(permit.TryAccept(grant, 1, stalePrepared, out _)).IsFalse();
        await Assert.That(permit.TryCapture(out _)).IsFalse();

        var prepared = TimeProvider.System.GetTimestamp();
        await Assert.That(permit.TryAccept(grant, 1, prepared, out var receipt)).IsTrue();
        await Assert.That(receipt).IsEqualTo(new CacheReadPermitAcceptance(1, 0, false));
    }

    [Test]
    [NotInParallel]
    public async Task LeaseExpiresAtFifteenSecondsFromItsPrepareTimestamp()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        using var permit = new CacheReadPermit(TimeProvider.System, UnitAdmissionOptions.Permit());
        var clock = TimeProvider.System;
        var prepared = clock.GetTimestamp();
        await WaitForMeasuredAgeAsync(clock, prepared, GrantPreparationDelay, linked.Token);
        var acceptStartedAt = clock.GetTimestamp();
        var accepted = permit.TryAccept(Guid.NewGuid(), 1, prepared, out _);
        await Assert.That(accepted).IsTrue();
        await Assert.That(permit.TryCapture(out var revision)).IsTrue();
        await Assert.That(revision).IsEqualTo(1);

        var ageAtReceipt = clock.GetElapsedTime(prepared, acceptStartedAt);
        await Assert.That(ageAtReceipt >= GrantPreparationDelay).IsTrue();
        await Assert.That(ageAtReceipt < CacheReadPermitLimits.PrepareValidity).IsTrue();
        await WaitForMeasuredAgeAsync(clock, prepared, CacheReadPermitLimits.LeaseValidity + Margin, linked.Token);

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

    private static async Task WaitForPreparedAgeAsync(TimeProvider clock, long prepared, TimeSpan minimumAge,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = clock.GetElapsedTime(prepared, clock.GetTimestamp());
            if (elapsed >= minimumAge)
            {
                return;
            }

            var remaining = minimumAge - elapsed;
            await Task.Delay(remaining + Margin, clock, cancellationToken);
        }
    }

    private static async Task WaitForMeasuredAgeAsync(TimeProvider clock, long prepared, TimeSpan minimumAge,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = minimumAge - clock.GetElapsedTime(prepared, clock.GetTimestamp());
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(remaining < MinimumWait ? MinimumWait : remaining, clock, cancellationToken);
        }
    }
}
