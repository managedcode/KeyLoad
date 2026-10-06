namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Measures the actual C# database clients inside the Aspire-owned TUnit workload container.</summary>
internal sealed class NativeClientWorkloadTests
{
    private const int SuccessfulWorkloadExit = 0;

    [Test]
    public async Task NativeClientWorkloadProducesSuccessfulOriginalReports()
    {
        var exit = await ComparisonApplication.RunAsync([], TestContext.Current!.Execution.CancellationToken)
            .ConfigureAwait(false);
        await Assert.That(exit).IsEqualTo(SuccessfulWorkloadExit);
    }
}
