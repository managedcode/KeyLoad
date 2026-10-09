using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>AC-METH-005: exclusive native RF3 correctness during one million acknowledged creates.</summary>
[Category(HeavyDocumentLoadProtocol.Category)]
[NotInParallel]
internal sealed class HeavyDocumentLoadRf3Tests : IAsyncDisposable
{
    private readonly ClusterFixture fixture;
    private readonly HeavyDocumentLoadScenario scenario;
    private int disposed;
    public HeavyDocumentLoadRf3Tests()
    {
        var limits = new DatabaseLimits
        {
            MaxScanRecords = HeavyDocumentLoadProtocol.Records,
            MaxQueryReadBytes = HeavyDocumentLoadProtocol.QueryReadBytes
        };
        fixture = new ClusterFixture(limits, HeavyDocumentLoadProtocol.SnapshotThreshold);
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
        { return; }
        var failures = new List<Exception>();
        var scenarioDisposal = scenario.DisposeAsync().AsTask();
        await ServerFailureObserver.ObserveAsync(() => scenarioDisposal, failures).ConfigureAwait(false);
        var fixtureDisposal = fixture.DisposeAsync().AsTask();
        await ServerFailureObserver.ObserveAsync(() => fixtureDisposal, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
