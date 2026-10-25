using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.DocumentStorage;

/// <summary>AC-METH-005: exclusive execution Orleans correctness during one million acknowledged creates.</summary>
[Category(HeavyDocumentLoadProtocol.Category)]
[NotInParallel]
internal sealed class HeavyDocumentLoadClusterTests : IAsyncDisposable
{
    private readonly ClusterFixture fixture;
    private readonly HeavyDocumentLoadScenario scenario;
    private int disposed;
    public HeavyDocumentLoadClusterTests()
    {
        fixture = new ClusterFixture();
        scenario = new(fixture);
    }

    [Test]
    public async Task MillionAcknowledgedDocumentsRemainCorrectDuringSdkAndOfficialMcpReads()
    {
        HeavyDocumentLoadAdmission.ValidateRuntime();
        var failures = new List<Exception>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(HeavyDocumentLoadProtocol.Deadline);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await fixture.InitializeAsync().ConfigureAwait(false);
            await scenario.RunAsync(deadline.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        var failures = new List<Exception>();
        var scenarioDisposal = scenario.DisposeAsync().AsTask();
        await ServerFailureObserver.ObserveAsync(() => scenarioDisposal, failures).ConfigureAwait(false);
        var fixtureDisposal = fixture.DisposeAsync().AsTask();
        await ServerFailureObserver.ObserveAsync(() => fixtureDisposal, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
