namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadExecutionBudgetAbsoluteDeadlineTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private const string OriginalFailure = "The original metadata reply was not observed.";
    private const int QuerySeconds = 20;
    private const int ElapsedSeconds = 2;
    private const int NarrowSeconds = 8;
    private const int LaterSeconds = 15;
    private const int ExhaustedSeconds = 9;

    [Test]
    public async Task AbsoluteDeadlineIsCapturedOnceAndOnlyNarrowsWithoutChangingMonotonicExpiry()
    {
        var clock = new ControlledReadClock(Epoch);
        var work = Create(clock, CancellationToken.None);
        var original = Epoch.AddSeconds(QuerySeconds);
        await Assert.That(work.ExpiresAt).IsEqualTo(original);
        clock.Advance(TimeSpan.FromSeconds(ElapsedSeconds));
        await Assert.That(work.ExpiresAt).IsEqualTo(original);
        var shorter = Epoch.AddSeconds(NarrowSeconds);
        work.ConstrainLifetime(shorter);
        await Assert.That(work.ExpiresAt).IsEqualTo(shorter);
        work.ConstrainLifetime(Epoch.AddSeconds(LaterSeconds));
        await Assert.That(work.ExpiresAt).IsEqualTo(shorter);
        clock.MoveUtc(Epoch.AddDays(1));
        await Assert.That(work.ExpiresAt).IsEqualTo(shorter);
        work.Check();
        clock.Advance(TimeSpan.FromSeconds(ExhaustedSeconds));
        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => { _ = work.ExpiresAt; });
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task LaterConstraintAndBackwardUtcCannotRenewOriginalDeadline()
    {
        var clock = new ControlledReadClock(Epoch);
        var work = Create(clock, CancellationToken.None);
        var original = Epoch.AddSeconds(QuerySeconds);
        clock.Advance(TimeSpan.FromSeconds(ElapsedSeconds));
        clock.MoveUtc(Epoch.AddDays(-1));
        work.ConstrainLifetime(original.AddDays(1));
        await Assert.That(work.ExpiresAt).IsEqualTo(original);
        clock.Advance(TimeSpan.FromSeconds(QuerySeconds));
        var exhausted = Assert.ThrowsExactly<KeyLoadException>(work.Check);
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task DeadlineGetterPreservesOriginalCancellationAndFirstIndeterminateFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var work = Create(new ControlledReadClock(Epoch), cancellation.Token);
        await cancellation.CancelAsync();
        var canceled = Assert.ThrowsExactly<OperationCanceledException>(() => { _ = work.ExpiresAt; });
        await Assert.That(canceled.CancellationToken).IsEqualTo(cancellation.Token);
        var original = Errors.Fail(ErrorCode.UnknownWriteOutcome, OriginalFailure);
        work.FailIndeterminateMetadata(original);
        var terminal = Assert.ThrowsExactly<KeyLoadException>(() => { _ = work.ExpiresAt; });
        await Assert.That(ReferenceEquals(terminal, original)).IsTrue();
    }

    private static ReadExecutionBudget Create(ControlledReadClock clock, CancellationToken token)
        => new(UnitExecutionOptions.DatabaseLimits(new()
        {
            QueryDeadlineSeconds = QuerySeconds
        }), clock, token);
}
