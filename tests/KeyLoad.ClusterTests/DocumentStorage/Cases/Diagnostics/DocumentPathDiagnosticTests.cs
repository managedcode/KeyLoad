namespace KeyLoad.ClusterTests.DocumentStorage;

/// <summary>Real bounded SDK workloads retain local diagnostic timings and complete document oracles.</summary>
[NotInParallel]
internal sealed class DocumentPathDiagnosticTests
{
    private const int LargeBatchRecords = 1024;

    [Test]
    public Task TenThousandDocuments() => DocumentPathDiagnosticScenario.RunAsync(10_000, 1,
        TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task TwentyThousandDocuments() => DocumentPathDiagnosticScenario.RunAsync(20_000, 1,
        TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task FiftyThousandDocuments() => DocumentPathDiagnosticScenario.RunAsync(50_000, 1,
        TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task TwoHundredFiftyThousandDocuments() => DocumentPathDiagnosticScenario.RunAsync(250_000, 1,
        TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task TwoHundredFiftyThousandDocumentsInBatchesOf1024() => DocumentPathDiagnosticScenario.RunAsync(250_000, 1,
        TestContext.Current!.Execution.CancellationToken, LargeBatchRecords);

    [Test]
    public Task TwentyThousandDocumentsAcrossThreeNodes() => DocumentPathDiagnosticScenario.RunAsync(20_000, 3,
        TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task OneHundredThousandDocumentsAcrossThreeNodes() => DocumentPathDiagnosticScenario.RunAsync(100_000, 3,
        TestContext.Current!.Execution.CancellationToken);
}
