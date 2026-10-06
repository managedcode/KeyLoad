using System.Diagnostics;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;
using Orleans.DurableJobs;

namespace KeyLoad.UnitTests.Features.Messaging;

[NativeSagaTimeoutDataSource]
[NotInParallel]
internal sealed class NativeSagaTimeoutTraceIsolationTests(NativeSagaTimeoutFixture fixture)
{
    private const string RootPrincipalId = "root";
    private const string CallerActivityName = "native-saga-caller";
    private const string SensitiveTraceState = "tenant=private";

    [Test]
    public async Task NativeScheduleDoesNotPersistAmbientCallerTraceState()
    {
        var saga = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture, RootPrincipalId, deadlineInFuture: true);
        var dueTime = TimeProvider.System.GetUtcNow().Add(fixture.TestProfile.HeldJobDelay);
        using var caller = new Activity(CallerActivityName);
        caller.SetIdFormat(ActivityIdFormat.W3C);
        caller.TraceStateString = SensitiveTraceState;
        caller.Start();
        var scheduled = await fixture.JobHarness.ScheduleAsync(saga.Hint, dueTime, CancellationToken.None);
        caller.Stop();

        await AssertIsolatedTraceAsync(scheduled, caller.TraceId.ToString(), saga.Hint);
        var shards = await fixture.JobHarness.CaptureOwnedShardsAsync(dueTime, CancellationToken.None);
        await fixture.JobHarness.WaitUntilSettledAsync(shards, CancellationToken.None);
    }

    private static async Task AssertIsolatedTraceAsync(DurableJob job, string callerTraceId, DueWorkHint hint)
    {
        var validTraceParent = ActivityContext.TryParse(job.TraceParent, job.TraceState, true, out var traceContext);
        await Assert.That(validTraceParent).IsTrue();
        await Assert.That(traceContext.TraceId.ToString() == callerTraceId).IsFalse();
        await Assert.That(job.TraceState).IsEqualTo(string.Empty);
        await Assert.That(traceContext.TraceFlags).IsEqualTo(ActivityTraceFlags.None);
        await Assert.That(NativeSagaTimeoutJobContract.Parse(job.Metadata)).IsEqualTo(hint);
        await Assert.That(job.Metadata!.Any(pair => pair.Key.Contains(SensitiveTraceState, StringComparison.Ordinal)
            || pair.Value.Contains(SensitiveTraceState, StringComparison.Ordinal))).IsFalse();
    }
}
