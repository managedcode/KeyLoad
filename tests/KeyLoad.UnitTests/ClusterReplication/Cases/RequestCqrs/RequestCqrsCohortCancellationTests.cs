namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
[NotInParallel]
internal sealed class RequestCqrsCohortCancellationTests(RequestCqrsCohortRuntimeFixture runtime)
{
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(20);

    [Test]
    public async Task CallerCancellationBeforeDispatchAndInsideNativePayloadSettlesBeforeHealthyLookup()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await using var scenario = await RequestCqrsCohortScenario.StartAsync(runtime, deadline.Token);
        await VerifyCancellationBeforeDispatchAsync(scenario);
        await VerifyCancellationInsideNativePayloadAsync(scenario);
        scenario.PublishCompatibleRemoteOne(1);
        var address = await scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, deadline.Token);
        await Assert.That(address).IsEqualTo(SiloAddress.FromParsableString(runtime.RuntimeAddress(1)));
    }

    private static Task VerifyCancellationBeforeDispatchAsync(RequestCqrsCohortScenario scenario)
    {
        var gate = new RequestCqrsCohortNativeGate();
        scenario.RemoteOne.SetHeaderGate(gate);
        return VerifyCancellationAsync(scenario, gate);
    }

    private static Task VerifyCancellationInsideNativePayloadAsync(RequestCqrsCohortScenario scenario)
    {
        var gate = new RequestCqrsCohortNativeGate();
        scenario.PublishCompatibleRemoteOne(1);
        scenario.RemoteOne.SetBodyGate(gate);
        return VerifyCancellationAsync(scenario, gate);
    }

    private static async Task VerifyCancellationAsync(
        RequestCqrsCohortScenario scenario, RequestCqrsCohortNativeGate gate)
    {
        using var cancellation = new CancellationTokenSource(TestBound, TimeProvider.System);
        Task<SiloAddress>? attempt = null;
        var failures = new List<Exception>();
        await RequestCqrsCohortCleanup.CaptureAsync(async () =>
        {
            attempt = scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, cancellation.Token);
            await AssertCancelledAttemptAsync(attempt, gate, cancellation);
        }, failures);

        await cancellation.CancelAsync();
        gate.Release();
        await RequestCqrsCohortAttemptSettlement.JoinCancelledAsync(attempt, gate, failures, cancellation.Token);
        RequestCqrsCohortCleanup.ThrowIfAny(failures);
    }

    private static async Task AssertCancelledAttemptAsync(
        Task<SiloAddress> attempt, RequestCqrsCohortNativeGate gate, CancellationTokenSource cancellation)
    {
        await gate.Entered.WaitAsync(TestBound, TimeProvider.System);
        await cancellation.CancelAsync();
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => attempt);
        await gate.Aborted.WaitAsync(TestBound, TimeProvider.System);
    }
}
