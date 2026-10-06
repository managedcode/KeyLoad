namespace KeyLoad.UnitTests.Features.Messaging;

[NativeSagaTimeoutDataSource]
[NotInParallel]
internal sealed class NativeSagaTimeoutRestartTests(NativeSagaTimeoutFixture fixture)
{
    private const int RevisionAfterTimeout = 2;
    private const int WaitingRevision = 1;
    private const int NoScheduledJobs = 0;
    private const string TimeoutPayload = "{\"action\":\"release-reservation\"}";
    private const string TimeoutHeaders = "{\"reason\":\"deadline\"}";

    [Test]
    public async Task NativeRuntimeRestartReclaimsRetainedJobAndCommitsOneStableTimeout()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var saga = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture,
            NativeSagaTimeoutTestData.RootPrincipalId);
        var dueTime = TimeProvider.System.GetUtcNow().Add(fixture.TestProfile.RestartHeldJobDelay);
        var job = await fixture.JobHarness.ScheduleAsync(saga.Hint, dueTime, cancellationToken);
        var originalShards = await fixture.JobHarness.CaptureOwnedShardsAsync(dueTime, cancellationToken);
        await Assert.That(await NativeSagaTimeoutJobHarness.CountScheduledJobsAsync(
            originalShards, cancellationToken)).IsGreaterThan(NoScheduledJobs);
        var originalFence = await NativeSagaTimeoutJournalFence.ReadAsync(fixture, job, cancellationToken);
        await Assert.That(originalFence.NativeOwner).IsEqualTo(originalFence.LocalSilo);

        await fixture.RestartRuntimeAsync(cancellationToken);
        var recovered = await NativeSagaTimeoutRestartWork.WaitForReclaimAsync(
            fixture, job, originalFence, cancellationToken);
        await Assert.That(recovered.Shard.Id).IsEqualTo(job.ShardId);
        await Assert.That(recovered.Fence.JournalName).IsEqualTo(originalFence.JournalName);
        await Assert.That(recovered.Fence.InstanceId).IsEqualTo(originalFence.InstanceId);
        await Assert.That(recovered.Fence.NativeOwner).IsNotEqualTo(originalFence.NativeOwner);
        await Assert.That(recovered.Fence.NativeMetadataETag).IsNotEqualTo(originalFence.NativeMetadataETag);
        await Assert.That(recovered.Fence.CanonicalOwnerGeneration)
            .IsGreaterThan(originalFence.CanonicalOwnerGeneration);
        await Assert.That(await NativeSagaTimeoutJobHarness.CountScheduledJobsAsync(
            [recovered.Shard], cancellationToken)).IsGreaterThan(NoScheduledJobs);

        await NativeSagaTimeoutRestartWork.WaitForTimeoutAsync(fixture, saga, job, cancellationToken);
        await NativeSagaTimeoutRestartWork.WaitForShardsToSettleAsync(fixture,
            [recovered.Shard], cancellationToken);
        await AssertTimeoutEffectAsync(saga);
    }

    private async Task AssertTimeoutEffectAsync(NativeSagaTimeoutCase saga)
    {
        var current = fixture.Database.Database.InspectSaga(
            NativeSagaTimeoutTestData.RootPrincipalId, saga.Lane, saga.Id);
        await Assert.That(current!.Phase).IsEqualTo(SagaPhase.TimedOut);
        await Assert.That(current.Revision).IsEqualTo(RevisionAfterTimeout);
        var message = fixture.Database.Database.InspectMessage(NativeSagaTimeoutTestData.RootPrincipalId,
            saga.TimeoutLane, NativeSagaTimeoutTestData.TimeoutMessageId(saga.Id, WaitingRevision));
        await Assert.That(message).IsNotNull();
        var actual = message!;
        await Assert.That(actual.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.PayloadJson).IsEqualTo(TimeoutPayload);
        await Assert.That(actual.HeadersJson).IsEqualTo(TimeoutHeaders);
        await Assert.That(actual.Metadata.ExpiresAt)
            .IsEqualTo(saga.Deadline.Add(NativeSagaTimeoutTestData.SagaTimeoutTtlValue));
    }
}
