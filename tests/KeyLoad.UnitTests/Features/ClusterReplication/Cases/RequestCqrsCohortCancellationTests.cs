namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
internal sealed class RequestCqrsCohortCancellationTests(RequestCqrsCohortRuntimeFixture runtime)
{
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(20);

    [Test]
    public async Task CallerCancellationDuringHeadersAndBodySettlesBeforeHealthyLookup()
    {
        using var deadline = new CancellationTokenSource(TestBound);
        await using var scenario = await RequestCqrsCohortScenario.StartAsync(runtime, deadline.Token);
        await VerifyCancellationAtHeadersAsync(scenario);
        await VerifyCancellationAtBodyAsync(scenario);
        scenario.PublishCompatibleRemoteOne(1);
        var address = await scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, deadline.Token);
        await Assert.That(address).IsEqualTo(SiloAddress.FromParsableString(runtime.RuntimeAddress(1)));
    }

    private static Task VerifyCancellationAtHeadersAsync(RequestCqrsCohortScenario scenario)
    {
        var gate = new RequestCqrsCohortHttpGate();
        scenario.RemoteOne.SetHeaderGate(gate);
        return VerifyCancellationAsync(scenario, gate);
    }

    private static Task VerifyCancellationAtBodyAsync(RequestCqrsCohortScenario scenario)
    {
        var gate = new RequestCqrsCohortHttpGate();
        scenario.PublishCompatibleRemoteOne(1);
        scenario.RemoteOne.SetBodyGate(gate);
        return VerifyCancellationAsync(scenario, gate);
    }

    private static async Task VerifyCancellationAsync(
        RequestCqrsCohortScenario scenario, RequestCqrsCohortHttpGate gate)
    {
        using var cancellation = new CancellationTokenSource(TestBound);
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
        Task<SiloAddress> attempt, RequestCqrsCohortHttpGate gate, CancellationTokenSource cancellation)
    {
        await gate.Entered.WaitAsync(TestBound);
        await cancellation.CancelAsync();
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => attempt);
        await gate.Aborted.WaitAsync(TestBound);
    }
}
