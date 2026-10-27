using System.Diagnostics;
using KeyLoad.Clustering;
using KeyLoad.Core.Messaging;
using Orleans.DurableJobs;

namespace KeyLoad.FunctionalTests.Messaging;

[SagaTimeoutDataSource]
[NotInParallel]
internal sealed class SagaTimeoutTraceIsolationTests(SagaTimeoutFixture fixture)
{
    private const string SagaPrincipalId = SagaTimeoutTestData.SagaPrincipalId;
    private const string CallerActivityName = "execution-saga-caller";
    private const string SensitiveTraceState = "tenant=private";

    [Test]
    public async Task ScheduleDoesNotPersistAmbientCallerTraceState()
    {
        var saga = SagaTimeoutTestData.CreateWaitingSaga(fixture, SagaPrincipalId);
        var dueTime = saga.Hint.DueAt;
        using var caller = new Activity(CallerActivityName);
        caller.SetIdFormat(ActivityIdFormat.W3C);
        caller.TraceStateString = SensitiveTraceState;
        caller.Start();
        var scheduled = await fixture.JobHarness.ScheduleAsync(saga.Hint, dueTime, CancellationToken.None);
        caller.Stop();

        await AssertIsolatedTraceAsync(scheduled, caller.TraceId.ToString(), saga.Hint);
        var shards = (await fixture.JobHarness.CaptureOwnedShardsAsync(dueTime, CancellationToken.None))
            .Where(shard => shard.Id == scheduled.ShardId).ToArray();
        try
        {
            await fixture.JobHarness.WaitUntilSettledAsync(shards, CancellationToken.None);
        }
        catch (TimeoutException failure)
        {
            var current = fixture.Database.Database.InspectSaga(SagaPrincipalId, saga.Lane, saga.Id);
            var counts = new List<string>();
            foreach (var shard in shards)
            {
                counts.Add($"{shard.Id}: {await shard.GetJobCountAsync()} jobs, scheduled={shard.Id == scheduled.ShardId}");
            }
            throw new TimeoutException($"{failure.Message} Saga phase: {current?.Phase}; {string.Join("; ", counts)}", failure);
        }
        var actual = fixture.Database.Database.InspectSaga(SagaPrincipalId, saga.Lane, saga.Id);
        await Assert.That(actual!.Phase).IsEqualTo(SagaPhase.TimedOut);
    }

    private static async Task AssertIsolatedTraceAsync(DurableJob job, string callerTraceId, DueWorkHint hint)
    {
        var validTraceParent = ActivityContext.TryParse(job.TraceParent, job.TraceState, true, out var traceContext);
        await Assert.That(validTraceParent).IsTrue();
        await Assert.That(traceContext.TraceId.ToString() == callerTraceId).IsFalse();
        await Assert.That(job.TraceState).IsEqualTo(string.Empty);
        await Assert.That(traceContext.TraceFlags).IsEqualTo(ActivityTraceFlags.None);
        await Assert.That(SagaTimeoutJobContract.Parse(job.Metadata)).IsEqualTo(hint);
        await Assert.That(job.Metadata!.Any(pair => pair.Key.Contains(SensitiveTraceState, StringComparison.Ordinal)
            || pair.Value.Contains(SensitiveTraceState, StringComparison.Ordinal))).IsFalse();
    }
}
