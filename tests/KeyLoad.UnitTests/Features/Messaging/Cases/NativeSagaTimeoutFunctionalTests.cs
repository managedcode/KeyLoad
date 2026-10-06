namespace KeyLoad.UnitTests.Features.Messaging;

[NativeSagaTimeoutDataSource]
[NotInParallel]
internal sealed class NativeSagaTimeoutFunctionalTests(NativeSagaTimeoutFixture fixture)
{
    private const string SagaPrincipalId = NativeSagaTimeoutTestData.SagaPrincipalId;
    private const int RevisionOne = 1;
    private const int RevisionAfterTimeout = RevisionOne + RevisionOne;
    private const int NoScheduledJobs = 0;
    private const string TimeoutCompletionFailure = "The native durable job did not commit the saga timeout before the test bound.";

    [Test]
    public async Task NativeJobExpiresCanonicalSagaAndEnqueuesExactlyOneStableTimeout()
    {
        var saga = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
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
        await Assert.That(expired.StateJson).IsEqualTo(NativeSagaTimeoutTestData.SagaState);
        await Assert.That(expired.Deadline).IsEqualTo(saga.Deadline);

        var messageId = NativeSagaTimeoutTestData.TimeoutMessageId(saga.Id, RevisionOne);
        var message = fixture.Database.Database.InspectMessage(SagaPrincipalId, saga.TimeoutLane, messageId);
        await Assert.That(message).IsNotNull();
        await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(message.PayloadJson).IsEqualTo(NativeSagaTimeoutTestData.TimeoutPayload);
        await Assert.That(message.HeadersJson).IsEqualTo(NativeSagaTimeoutTestData.TimeoutHeaders);
        await Assert.That(message.Metadata.ExpiresAt).IsEqualTo(saga.Deadline.Add(NativeSagaTimeoutTestData.SagaTimeoutTtlValue));
    }

    [Test]
    public async Task NativeJobObservesCompletionCommittedAfterScheduling()
    {
        var stale = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var staleJobDueAt = NativeSagaTimeoutTestData.MutatableNativeJobDueAt(fixture);
        await fixture.JobHarness.ScheduleAsync(stale.Hint, staleJobDueAt, CancellationToken.None);
        var staleShards = await fixture.JobHarness.CaptureOwnedShardsAsync(staleJobDueAt, CancellationToken.None);
        await Assert.That(await NativeSagaTimeoutJobHarness.CountScheduledJobsAsync(staleShards)).IsGreaterThan(NoScheduledJobs);
        NativeSagaTimeoutTestData.CompleteSaga(fixture, stale);

        var control = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var controlDueAt = ControlJobDueAt(staleJobDueAt, control);
        await fixture.JobHarness.ScheduleAsync(control.Hint, controlDueAt, CancellationToken.None);
        var controlShards = await fixture.JobHarness.CaptureOwnedShardsAsync(controlDueAt, CancellationToken.None);
        await Assert.That(await NativeSagaTimeoutJobHarness.CountScheduledJobsAsync(controlShards)).IsGreaterThan(NoScheduledJobs);
        _ = await WaitForTimedOutSagaAsync(control.Lane, control.Id);
        var ownedShards = NativeSagaTimeoutJobHarness.MergeShards(staleShards, controlShards);
        await fixture.JobHarness.WaitUntilSettledAsync(ownedShards, CancellationToken.None);

        var current = fixture.Database.Database.InspectSaga(SagaPrincipalId, stale.Lane, stale.Id);
        await Assert.That(current!.Phase).IsEqualTo(SagaPhase.Completed);
        await Assert.That(current.Revision).IsEqualTo(RevisionAfterTimeout);
        await Assert.That(fixture.Database.Database.InspectMessage(SagaPrincipalId, stale.TimeoutLane,
            NativeSagaTimeoutTestData.TimeoutMessageId(stale.Id, RevisionOne))).IsNull();
    }

    [Test]
    public async Task NativeJobObservesSagaCancellationBeforeTimeout()
    {
        var stale = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var staleJobDueAt = NativeSagaTimeoutTestData.MutatableNativeJobDueAt(fixture);
        await fixture.JobHarness.ScheduleAsync(stale.Hint, staleJobDueAt, CancellationToken.None);
        var staleShards = await fixture.JobHarness.CaptureOwnedShardsAsync(staleJobDueAt, CancellationToken.None);
        await Assert.That(await NativeSagaTimeoutJobHarness.CountScheduledJobsAsync(staleShards)).IsGreaterThan(NoScheduledJobs);
        NativeSagaTimeoutTestData.CancelSaga(fixture, stale);

        var control = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var controlDueAt = ControlJobDueAt(staleJobDueAt, control);
        await fixture.JobHarness.ScheduleAsync(control.Hint, controlDueAt, CancellationToken.None);
        var controlShards = await fixture.JobHarness.CaptureOwnedShardsAsync(controlDueAt, CancellationToken.None);
        await Assert.That(await NativeSagaTimeoutJobHarness.CountScheduledJobsAsync(controlShards)).IsGreaterThan(NoScheduledJobs);
        _ = await WaitForTimedOutSagaAsync(control.Lane, control.Id);
        var ownedShards = NativeSagaTimeoutJobHarness.MergeShards(staleShards, controlShards);
        await fixture.JobHarness.WaitUntilSettledAsync(ownedShards, CancellationToken.None);

        var current = fixture.Database.Database.InspectSaga(SagaPrincipalId, stale.Lane, stale.Id);
        await Assert.That(current!.Phase).IsEqualTo(SagaPhase.Cancelled);
        await Assert.That(current.Revision).IsEqualTo(RevisionAfterTimeout);
        await Assert.That(fixture.Database.Database.InspectMessage(SagaPrincipalId, stale.TimeoutLane,
            NativeSagaTimeoutTestData.TimeoutMessageId(stale.Id, RevisionOne))).IsNull();
    }

    [Test]
    public async Task NativeJobObservesPersistedCreatorRevocationBeforeTimeout()
    {
        NativeSagaTimeoutTestData.ConfigureRevocableCreator(fixture);
        var stale = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture, NativeSagaTimeoutTestData.RevocableCreator);
        var staleJobDueAt = NativeSagaTimeoutTestData.MutatableNativeJobDueAt(fixture);
        await fixture.JobHarness.ScheduleAsync(stale.Hint, staleJobDueAt, CancellationToken.None);
        var staleShards = await fixture.JobHarness.CaptureOwnedShardsAsync(staleJobDueAt, CancellationToken.None);
        await Assert.That(await NativeSagaTimeoutJobHarness.CountScheduledJobsAsync(staleShards)).IsGreaterThan(NoScheduledJobs);
        NativeSagaTimeoutTestData.RevokeCreator(fixture);

        var control = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var controlDueAt = ControlJobDueAt(staleJobDueAt, control);
        await fixture.JobHarness.ScheduleAsync(control.Hint, controlDueAt, CancellationToken.None);
        var controlShards = await fixture.JobHarness.CaptureOwnedShardsAsync(controlDueAt, CancellationToken.None);
        await Assert.That(await NativeSagaTimeoutJobHarness.CountScheduledJobsAsync(controlShards)).IsGreaterThan(NoScheduledJobs);
        _ = await WaitForTimedOutSagaAsync(control.Lane, control.Id);
        var ownedShards = NativeSagaTimeoutJobHarness.MergeShards(staleShards, controlShards);
        await fixture.JobHarness.WaitUntilSettledAsync(ownedShards, CancellationToken.None);

        var current = fixture.Database.Database.InspectSaga(SagaPrincipalId, stale.Lane, stale.Id);
        await Assert.That(current!.Phase).IsEqualTo(SagaPhase.Waiting);
        await Assert.That(current.Revision).IsEqualTo(RevisionOne);
        await Assert.That(fixture.Database.Database.InspectMessage(SagaPrincipalId, stale.TimeoutLane,
            NativeSagaTimeoutTestData.TimeoutMessageId(stale.Id, RevisionOne))).IsNull();
    }

    [Test]
    public async Task NativeJobObservesFutureCanonicalDeadlineBeforeTimeout()
    {
        var future = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId, deadlineInFuture: true);
        var staleJobDueAt = TimeProvider.System.GetUtcNow().Add(fixture.TestProfile.HeldJobDelay);
        await fixture.JobHarness.ScheduleAsync(future.Hint, staleJobDueAt, CancellationToken.None);
        var staleShards = await fixture.JobHarness.CaptureOwnedShardsAsync(staleJobDueAt, CancellationToken.None);
        await Assert.That(await NativeSagaTimeoutJobHarness.CountScheduledJobsAsync(staleShards)).IsGreaterThan(NoScheduledJobs);

        var control = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var controlDueAt = ControlJobDueAt(staleJobDueAt, control);
        await fixture.JobHarness.ScheduleAsync(control.Hint, controlDueAt, CancellationToken.None);
        var controlShards = await fixture.JobHarness.CaptureOwnedShardsAsync(controlDueAt, CancellationToken.None);
        await Assert.That(await NativeSagaTimeoutJobHarness.CountScheduledJobsAsync(controlShards)).IsGreaterThan(NoScheduledJobs);
        _ = await WaitForTimedOutSagaAsync(control.Lane, control.Id);
        var ownedShards = NativeSagaTimeoutJobHarness.MergeShards(staleShards, controlShards);
        await fixture.JobHarness.WaitUntilSettledAsync(ownedShards, CancellationToken.None);

        var current = fixture.Database.Database.InspectSaga(SagaPrincipalId, future.Lane, future.Id);
        await Assert.That(current!.Phase).IsEqualTo(SagaPhase.Waiting);
        await Assert.That(current.Revision).IsEqualTo(RevisionOne);
        await Assert.That(fixture.Database.Database.InspectMessage(SagaPrincipalId, future.TimeoutLane,
            NativeSagaTimeoutTestData.TimeoutMessageId(future.Id, RevisionOne))).IsNull();
    }

    private DateTimeOffset ControlJobDueAt(DateTimeOffset previousJobDueAt, NativeSagaTimeoutCase control)
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
