namespace KeyLoad.UnitTests.Messaging;

[SagaTimeoutDataSource]
[NotInParallel]
internal sealed class SagaTimeoutRestartTests(SagaTimeoutFixture fixture)
{
    private const int RevisionAfterTimeout = 2;
    private const int WaitingRevision = 1;
    private const int NoScheduledJobs = 0;
    private const string TimeoutPayload = "{\"action\":\"release-reservation\"}";
    private const string TimeoutHeaders = "{\"reason\":\"deadline\"}";

    [Test]
    public async Task RuntimeRestartReclaimsRetainedJobAndCommitsOneStableTimeout()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var saga = SagaTimeoutTestData.CreateWaitingSaga(fixture,
            SagaTimeoutTestData.SagaPrincipalId);
        var dueTime = TimeProvider.System.GetUtcNow().Add(fixture.TestProfile.RestartHeldJobDelay);
        var job = await fixture.JobHarness.ScheduleAsync(saga.Hint, dueTime, cancellationToken);
        var originalShards = await fixture.JobHarness.CaptureOwnedShardsAsync(dueTime, cancellationToken);
        await Assert.That(await SagaTimeoutJobHarness.CountScheduledJobsAsync(
            originalShards, cancellationToken)).IsGreaterThan(NoScheduledJobs);
        var originalFence = await SagaTimeoutJournalFence.ReadAsync(fixture, job, cancellationToken);
        await Assert.That(originalFence.RuntimeOwner).IsEqualTo(originalFence.LocalSilo);

        await fixture.RestartRuntimeAsync(cancellationToken);
        var recovered = await SagaTimeoutRestartWork.WaitForReclaimAsync(
            fixture, job, originalFence, cancellationToken);
        await Assert.That(recovered.Shard.Id).IsEqualTo(job.ShardId);
        await Assert.That(recovered.Fence.JournalName).IsEqualTo(originalFence.JournalName);
        await Assert.That(recovered.Fence.InstanceId).IsEqualTo(originalFence.InstanceId);
        await Assert.That(recovered.Fence.RuntimeOwner).IsNotEqualTo(originalFence.RuntimeOwner);
        await Assert.That(recovered.Fence.RuntimeMetadataETag).IsNotEqualTo(originalFence.RuntimeMetadataETag);
        await Assert.That(recovered.Fence.CanonicalOwnerGeneration)
            .IsGreaterThan(originalFence.CanonicalOwnerGeneration);
        await Assert.That(await SagaTimeoutJobHarness.CountScheduledJobsAsync(
            [recovered.Shard], cancellationToken)).IsGreaterThan(NoScheduledJobs);

        await SagaTimeoutRestartWork.WaitForTimeoutAsync(fixture, saga, job, cancellationToken);
        await SagaTimeoutRestartWork.WaitForShardsToSettleAsync(fixture,
            [recovered.Shard], cancellationToken);
        await AssertTimeoutEffectAsync(saga);
    }

    private async Task AssertTimeoutEffectAsync(SagaTimeoutCase saga)
    {
        var current = fixture.Database.Database.InspectSaga(
            SagaTimeoutTestData.SagaPrincipalId, saga.Lane, saga.Id);
        await Assert.That(current!.Phase).IsEqualTo(SagaPhase.TimedOut);
        await Assert.That(current.Revision).IsEqualTo(RevisionAfterTimeout);
        var message = fixture.Database.Database.InspectMessage(SagaTimeoutTestData.SagaPrincipalId,
            saga.TimeoutLane, SagaTimeoutTestData.TimeoutMessageId(saga.Id, WaitingRevision));
        await Assert.That(message).IsNotNull();
        var actual = message!;
        await Assert.That(actual.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.PayloadJson).IsEqualTo(TimeoutPayload);
        await Assert.That(actual.HeadersJson).IsEqualTo(TimeoutHeaders);
        await Assert.That(actual.Metadata.ExpiresAt)
            .IsEqualTo(saga.Deadline.Add(SagaTimeoutTestData.SagaTimeoutTtlValue));
    }
}
