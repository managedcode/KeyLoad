namespace KeyLoad.UnitTests.Features.Messaging;

[SagaTimeoutDataSource]
[NotInParallel]
internal sealed class SagaTimeoutFunctionalTests(SagaTimeoutFixture fixture)
{
    private const string SagaPrincipalId = SagaTimeoutTestData.SagaPrincipalId;
    private const int RevisionAfterTimeout = 1 + 1;
    private const string TimeoutCompletionFailure = "The execution durable job did not commit the saga timeout before the test bound.";

    [Test]
    public async Task JobExpiresCanonicalSagaAndEnqueuesExactlyOneStableTimeout()
    {
        var saga = SagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        using var deadline = new CancellationTokenSource(fixture.TestProfile.CompletionTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        var remaining = saga.Deadline - TimeProvider.System.GetUtcNow();
        while (remaining > TimeSpan.Zero)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)),
                TimeProvider.System, linked.Token);
            remaining = saga.Deadline - TimeProvider.System.GetUtcNow();
        }
        _ = await fixture.DispatchAsync(saga.Hint, linked.Token);
        _ = await fixture.DispatchAsync(saga.Hint, linked.Token);

        var expired = await WaitForTimedOutSagaAsync(saga.Lane, saga.Id);
        await Assert.That(expired.Revision).IsEqualTo(RevisionAfterTimeout);
        await Assert.That(expired.StateJson).IsEqualTo(SagaTimeoutTestData.SagaState);
        await Assert.That(expired.Deadline).IsEqualTo(saga.Deadline);

        var messageId = SagaTimeoutTestData.TimeoutMessageId(saga.Id, 1);
        var message = fixture.Database.Database.InspectMessage(SagaPrincipalId, saga.TimeoutLane, messageId);
        await Assert.That(message).IsNotNull();
        await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(message.PayloadJson).IsEqualTo(SagaTimeoutTestData.TimeoutPayload);
        await Assert.That(message.HeadersJson).IsEqualTo(SagaTimeoutTestData.TimeoutHeaders);
        await Assert.That(message.Metadata.ExpiresAt).IsEqualTo(saga.Deadline.Add(SagaTimeoutTestData.SagaTimeoutTtlValue));
    }

    [Test]
    public async Task JobObservesCompletionCommittedAfterScheduling()
    {
        var stale = SagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var staleJobDueAt = SagaTimeoutTestData.MutatableJobDueAt(fixture);
        await fixture.JobHarness.ScheduleAsync(stale.Hint, staleJobDueAt, CancellationToken.None);
        var staleShards = await fixture.JobHarness.CaptureOwnedShardsAsync(staleJobDueAt, CancellationToken.None);
        await Assert.That(await SagaTimeoutJobHarness.CountScheduledJobsAsync(staleShards)).IsGreaterThan(0);
        SagaTimeoutTestData.CompleteSaga(fixture, stale);

        var control = SagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var controlDueAt = ControlJobDueAt(staleJobDueAt, control);
        await fixture.JobHarness.ScheduleAsync(control.Hint, controlDueAt, CancellationToken.None);
        var controlShards = await fixture.JobHarness.CaptureOwnedShardsAsync(controlDueAt, CancellationToken.None);
        await Assert.That(await SagaTimeoutJobHarness.CountScheduledJobsAsync(controlShards)).IsGreaterThan(0);
        _ = await WaitForTimedOutSagaAsync(control.Lane, control.Id);
        var ownedShards = SagaTimeoutJobHarness.MergeShards(staleShards, controlShards);
        await fixture.JobHarness.WaitUntilSettledAsync(ownedShards, CancellationToken.None);

        var current = fixture.Database.Database.InspectSaga(SagaPrincipalId, stale.Lane, stale.Id);
        await Assert.That(current!.Phase).IsEqualTo(SagaPhase.Completed);
        await Assert.That(current.Revision).IsEqualTo(RevisionAfterTimeout);
        await Assert.That(fixture.Database.Database.InspectMessage(SagaPrincipalId, stale.TimeoutLane,
            SagaTimeoutTestData.TimeoutMessageId(stale.Id, 1))).IsNull();
    }

    [Test]
    public async Task JobObservesSagaCancellationBeforeTimeout()
    {
        var stale = SagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var staleJobDueAt = SagaTimeoutTestData.MutatableJobDueAt(fixture);
        await fixture.JobHarness.ScheduleAsync(stale.Hint, staleJobDueAt, CancellationToken.None);
        var staleShards = await fixture.JobHarness.CaptureOwnedShardsAsync(staleJobDueAt, CancellationToken.None);
        await Assert.That(await SagaTimeoutJobHarness.CountScheduledJobsAsync(staleShards)).IsGreaterThan(0);
        SagaTimeoutTestData.CancelSaga(fixture, stale);

        var control = SagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var controlDueAt = ControlJobDueAt(staleJobDueAt, control);
        await fixture.JobHarness.ScheduleAsync(control.Hint, controlDueAt, CancellationToken.None);
        var controlShards = await fixture.JobHarness.CaptureOwnedShardsAsync(controlDueAt, CancellationToken.None);
        await Assert.That(await SagaTimeoutJobHarness.CountScheduledJobsAsync(controlShards)).IsGreaterThan(0);
        _ = await WaitForTimedOutSagaAsync(control.Lane, control.Id);
        var ownedShards = SagaTimeoutJobHarness.MergeShards(staleShards, controlShards);
        await fixture.JobHarness.WaitUntilSettledAsync(ownedShards, CancellationToken.None);

        var current = fixture.Database.Database.InspectSaga(SagaPrincipalId, stale.Lane, stale.Id);
        await Assert.That(current!.Phase).IsEqualTo(SagaPhase.Cancelled);
        await Assert.That(current.Revision).IsEqualTo(RevisionAfterTimeout);
        await Assert.That(fixture.Database.Database.InspectMessage(SagaPrincipalId, stale.TimeoutLane,
            SagaTimeoutTestData.TimeoutMessageId(stale.Id, 1))).IsNull();
    }

    [Test]
    public async Task JobObservesPersistedCreatorRevocationBeforeTimeout()
    {
        SagaTimeoutTestData.ConfigureRevocableCreator(fixture);
        var stale = SagaTimeoutTestData.CreateWaitingSaga(fixture, SagaTimeoutTestData.RevocableCreator);
        var staleJobDueAt = SagaTimeoutTestData.MutatableJobDueAt(fixture);
        await fixture.JobHarness.ScheduleAsync(stale.Hint, staleJobDueAt, CancellationToken.None);
        var staleShards = await fixture.JobHarness.CaptureOwnedShardsAsync(staleJobDueAt, CancellationToken.None);
        await Assert.That(await SagaTimeoutJobHarness.CountScheduledJobsAsync(staleShards)).IsGreaterThan(0);
        SagaTimeoutTestData.RevokeCreator(fixture);

        var control = SagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var controlDueAt = ControlJobDueAt(staleJobDueAt, control);
        await fixture.JobHarness.ScheduleAsync(control.Hint, controlDueAt, CancellationToken.None);
        var controlShards = await fixture.JobHarness.CaptureOwnedShardsAsync(controlDueAt, CancellationToken.None);
        await Assert.That(await SagaTimeoutJobHarness.CountScheduledJobsAsync(controlShards)).IsGreaterThan(0);
        _ = await WaitForTimedOutSagaAsync(control.Lane, control.Id);
        var ownedShards = SagaTimeoutJobHarness.MergeShards(staleShards, controlShards);
        await fixture.JobHarness.WaitUntilSettledAsync(ownedShards, CancellationToken.None);

        var current = fixture.Database.Database.InspectSaga(SagaPrincipalId, stale.Lane, stale.Id);
        await Assert.That(current!.Phase).IsEqualTo(SagaPhase.Waiting);
        await Assert.That(current.Revision).IsEqualTo(1);
        await Assert.That(fixture.Database.Database.InspectMessage(SagaPrincipalId, stale.TimeoutLane,
            SagaTimeoutTestData.TimeoutMessageId(stale.Id, 1))).IsNull();
    }

    [Test]
    public async Task JobObservesFutureCanonicalDeadlineBeforeTimeout()
    {
        var future = SagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId, deadlineInFuture: true);
        var staleJobDueAt = TimeProvider.System.GetUtcNow().Add(fixture.TestProfile.HeldJobDelay);
        await fixture.JobHarness.ScheduleAsync(future.Hint, staleJobDueAt, CancellationToken.None);
        var staleShards = await fixture.JobHarness.CaptureOwnedShardsAsync(staleJobDueAt, CancellationToken.None);
        await Assert.That(await SagaTimeoutJobHarness.CountScheduledJobsAsync(staleShards)).IsGreaterThan(0);

        var control = SagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var controlDueAt = ControlJobDueAt(staleJobDueAt, control);
        await fixture.JobHarness.ScheduleAsync(control.Hint, controlDueAt, CancellationToken.None);
        var controlShards = await fixture.JobHarness.CaptureOwnedShardsAsync(controlDueAt, CancellationToken.None);
        await Assert.That(await SagaTimeoutJobHarness.CountScheduledJobsAsync(controlShards)).IsGreaterThan(0);
        _ = await WaitForTimedOutSagaAsync(control.Lane, control.Id);
        var ownedShards = SagaTimeoutJobHarness.MergeShards(staleShards, controlShards);
        await fixture.JobHarness.WaitUntilSettledAsync(ownedShards, CancellationToken.None);

        var current = fixture.Database.Database.InspectSaga(SagaPrincipalId, future.Lane, future.Id);
        await Assert.That(current!.Phase).IsEqualTo(SagaPhase.Waiting);
        await Assert.That(current.Revision).IsEqualTo(1);
        await Assert.That(fixture.Database.Database.InspectMessage(SagaPrincipalId, future.TimeoutLane,
            SagaTimeoutTestData.TimeoutMessageId(future.Id, 1))).IsNull();
    }

    private DateTimeOffset ControlJobDueAt(DateTimeOffset previousJobDueAt, SagaTimeoutCase control)
    {
        var plannedDueAt = previousJobDueAt.Add(fixture.TestProfile.HeldJobDelay);
        var canonicalDueAt = control.Deadline.Add(fixture.TestProfile.HeldJobDelay);
        return plannedDueAt >= canonicalDueAt ? plannedDueAt : canonicalDueAt;
    }

    private async Task<SagaInspection> WaitForTimedOutSagaAsync(QueueLaneRef lane, Guid sagaId)
    {
        using var deadline = new CancellationTokenSource(fixture.TestProfile.CompletionTimeout, TimeProvider.System);
        try
        {
            while (true)
            {
                var current = fixture.Database.Database.InspectSaga(SagaPrincipalId, lane, sagaId);
                if (current is { Phase: SagaPhase.TimedOut })
                {
                    return current;
                }
                await Task.Delay(fixture.TestProfile.PollInterval, TimeProvider.System, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException(TimeoutCompletionFailure);
        }
    }

}
