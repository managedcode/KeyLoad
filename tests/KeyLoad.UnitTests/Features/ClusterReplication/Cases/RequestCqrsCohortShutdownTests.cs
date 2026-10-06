using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
[NotInParallel]
internal sealed class RequestCqrsCohortShutdownTests(RequestCqrsCohortRuntimeFixture runtime)
{
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(20);

    [Test]
    public async Task AsyncDisposeStopsAdmissionAndJoinsGatedHttpAttempt()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await using var scenario = await RequestCqrsCohortScenario.StartAsync(runtime, deadline.Token);
        await VerifyShutdownAsync(scenario, synchronous: false);
    }

    [Test]
    public async Task SyncDisposeStartsSameShutdownAndAsyncDisposeJoinsIt()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await using var scenario = await RequestCqrsCohortScenario.StartAsync(runtime, deadline.Token);
        await VerifyShutdownAsync(scenario, synchronous: true);
    }

    private static async Task VerifyShutdownAsync(RequestCqrsCohortScenario scenario, bool synchronous)
    {
        var gate = new RequestCqrsCohortHttpGate();
        scenario.RemoteOne.SetHeaderGate(gate);
        var attempt = scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, CancellationToken.None);
        Task? disposal = null;
        var failures = new List<Exception>();
        await RequestCqrsCohortCleanup.CaptureAsync(async () =>
        {
            await gate.Entered.WaitAsync(TestBound, TimeProvider.System);
            if (synchronous)
            {
                DisposeSynchronously(scenario.Client);
            }

            disposal = scenario.Client.DisposeAsync().AsTask();
            await AssertAdmissionStoppedAsync(scenario);
            await Assert.That(scenario.Client.HasCompatibleCohort).IsFalse();
            await AssertShutdownCancellationAsync(attempt);
            await gate.Aborted.WaitAsync(TestBound, TimeProvider.System);
            await disposal.WaitAsync(TestBound, TimeProvider.System);
            await Assert.That(attempt.IsCompleted).IsTrue();
        }, failures);

        gate.Release();
        await RequestCqrsCohortAttemptSettlement.JoinShutdownAsync(attempt, gate, failures);
        await RequestCqrsCohortAttemptSettlement.JoinDisposalAsync(disposal, failures);
        RequestCqrsCohortCleanup.ThrowIfAny(failures);
    }

    private static void DisposeSynchronously(ReplicaSiloDiscoveryClient client)
        => client.Dispose();

    private static async Task AssertAdmissionStoppedAsync(RequestCqrsCohortScenario scenario)
    {
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, false, CancellationToken.None));
    }

    private static async Task AssertShutdownCancellationAsync(Task<SiloAddress> attempt)
    {
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => attempt);
    }
}
