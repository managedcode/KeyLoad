using KeyLoad.UnitTests.Features.Authorization;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDispatchCasFlow
{
    private const int Initial = QueueDispatchCasFixture.InitialVersion;
    private const int Step = QueueDispatchCasFixture.VersionStep;
    private const int ColdCycles = 2;
    private const int InvalidZero = 0;
    private const int InvalidNegative = -1;

    internal static async Task RunAsync(QueueDispatchCasFixture fixture)
    {
        await RefusalsAsync(fixture);
        var pause = fixture.Command(Guid.NewGuid(), Initial, true);
        var firstPause = fixture.Apply(pause);
        var paused = fixture.Definition with
        {
            Paused = true,
            SchemaVersion = Initial + Step
        };
        await QueueDispatchCasAssertions.SuccessAsync(firstPause, paused);
        await QueueDispatchCasAssertions.CurrentAsync(fixture, true, Initial + Step);
        await QueueDispatchCasAssertions.SuccessAsync(fixture.Apply(pause), fixture.Definition with
        {
            Paused = true,
            SchemaVersion = Initial + Step
        });
        await QueueDispatchCasAssertions.CurrentAsync(fixture, true, Initial + Step);
        var pauseOutcome = fixture.Outcome(pause.CommandId) ?? throw new InvalidOperationException();
        await QueueDispatchCasAssertions.FailureAsync(fixture,
            fixture.Apply(fixture.Command(pause.CommandId, Initial, false)), ErrorCode.Conflict, true, Initial + Step);
        await Assert.That(fixture.Outcome(pause.CommandId)!.SequenceEqual(pauseOutcome)).IsTrue();
        var stale = fixture.Command(Guid.NewGuid(), Initial, false);
        await QueueDispatchCasAssertions.FailureAsync(fixture, fixture.Apply(stale),
            ErrorCode.RevisionConflict, true, Initial + Step);
        var resume = fixture.Command(Guid.NewGuid(), Initial + Step, false);
        var firstResume = fixture.Apply(resume);
        var resumed = fixture.Definition with
        {
            Paused = false,
            SchemaVersion = Initial + Step + Step
        };
        await QueueDispatchCasAssertions.SuccessAsync(firstResume, resumed);
        await QueueDispatchCasAssertions.CurrentAsync(fixture, false, Initial + Step + Step);
        await QueueDispatchCasAssertions.SuccessAsync(fixture.Apply(resume), fixture.Definition with
        {
            Paused = false,
            SchemaVersion = Initial + Step + Step
        });
        await QueueDispatchCasAssertions.CurrentAsync(fixture, false, Initial + Step + Step);
        var resumeOutcome = fixture.Outcome(resume.CommandId) ?? throw new InvalidOperationException();
        var staleOutcome = fixture.Outcome(stale.CommandId) ?? throw new InvalidOperationException();
        for (var cycle = 0; cycle < ColdCycles; cycle++)
        {
            fixture.Reopen();
            await QueueDispatchCasAssertions.CurrentAsync(fixture, false, Initial + Step + Step);
            await QueueDispatchCasAssertions.SuccessAsync(fixture.Apply(pause), paused);
            await QueueDispatchCasAssertions.CurrentAsync(fixture, false, Initial + Step + Step);
            await QueueDispatchCasAssertions.SuccessAsync(fixture.Apply(resume), resumed);
            await QueueDispatchCasAssertions.CurrentAsync(fixture, false, Initial + Step + Step);
            await QueueDispatchCasAssertions.FailureAsync(fixture, fixture.Apply(stale),
                ErrorCode.RevisionConflict, false, Initial + Step + Step);
            await Assert.That(fixture.Outcome(pause.CommandId)!.SequenceEqual(pauseOutcome)).IsTrue();
            await Assert.That(fixture.Outcome(resume.CommandId)!.SequenceEqual(resumeOutcome)).IsTrue();
            await Assert.That(fixture.Outcome(stale.CommandId)!.SequenceEqual(staleOutcome)).IsTrue();
        }
        await HealthyAsync(fixture);
    }

    private static async Task RefusalsAsync(QueueDispatchCasFixture fixture)
    {
        foreach (var invalid in new[] { InvalidZero, InvalidNegative })
        {
            await QueueDispatchCasAssertions.FailureAsync(fixture,
                fixture.Apply(fixture.Command(Guid.NewGuid(), invalid, true)), ErrorCode.Validation, false, Initial);
        }
        await QueueDispatchCasAssertions.FailureAsync(fixture,
            fixture.Apply(fixture.Command(Guid.NewGuid(), Initial, false)), ErrorCode.Validation, false, Initial);
        await QueueDispatchCasAssertions.FailureAsync(fixture,
            fixture.Apply(fixture.Command(Guid.NewGuid(), Initial + Step, true)), ErrorCode.RevisionConflict, false, Initial);
        await QueueDispatchCasAssertions.FailureAsync(fixture, fixture.Apply(fixture.Command(Guid.NewGuid(), Initial,
            true, principal: ResourcePolicyUpdateFixture.ReaderId)), ErrorCode.PermissionDenied, false, Initial);
        await QueueDispatchCasAssertions.FailureAsync(fixture, fixture.Apply(fixture.Command(Guid.NewGuid(), Initial,
            true, queue: QueueDispatchCasFixture.MissingQueue)), ErrorCode.RevisionConflict, false, Initial);
        await QueueDispatchCasAssertions.FailureAsync(fixture, fixture.Apply(fixture.Command(Guid.NewGuid(), Initial,
            true, queue: ResourcePolicyUpdateFixture.Collection)), ErrorCode.Validation, false, Initial);
        await QueueDispatchCasAssertions.FailureAsync(fixture, fixture.PolicyPauseAttempt(),
            ErrorCode.UnsupportedCapability, false, Initial);
    }

    private static async Task HealthyAsync(QueueDispatchCasFixture fixture)
    {
        var version = Initial + Step + Step;
        var pause = fixture.Command(Guid.NewGuid(), version, true);
        await QueueDispatchCasAssertions.SuccessAsync(fixture.Apply(pause), fixture.Definition with
        {
            Paused = true,
            SchemaVersion = version + Step
        });
        await QueueDispatchCasAssertions.CurrentAsync(fixture, true, version + Step);
        var resume = fixture.Command(Guid.NewGuid(), version + Step, false);
        await QueueDispatchCasAssertions.SuccessAsync(fixture.Apply(resume), fixture.Definition with
        {
            Paused = false,
            SchemaVersion = version + Step + Step
        });
        await QueueDispatchCasAssertions.CurrentAsync(fixture, false, version + Step + Step);
        await QueueDispatchCasAssertions.SuccessAsync(fixture.Apply(pause), fixture.Definition with
        {
            Paused = true,
            SchemaVersion = version + Step
        });
        await QueueDispatchCasAssertions.CurrentAsync(fixture, false, version + Step + Step);
        await QueueDispatchCasAssertions.SuccessAsync(fixture.Apply(resume), fixture.Definition with
        {
            Paused = false,
            SchemaVersion = version + Step + Step
        });
        await QueueDispatchCasAssertions.CurrentAsync(fixture, false, version + Step + Step);
    }
}
