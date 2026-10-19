namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceBoundsTests
{
    [Test]
    public async Task AcScale016ProbeStopsAndJoinsWhenItsByteGrantIsExceeded()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await ScaleServerResourceBoundsFlow.RunAsync(diagnostic: false, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcScale016ProbeStopsAndJoinsWhenItsDiagnosticByteGrantIsExceeded()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await ScaleServerResourceBoundsFlow.RunAsync(diagnostic: true, TestContext.Current!.Execution.CancellationToken);
    }
}
