namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceBoundsTests
{
    [Test]
    public async Task AcScale016NativeProbeStopsAndJoinsWhenItsByteGrantIsExceeded()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await ScaleServerResourceBoundsFlow.RunAsync(diagnostic: false, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcScale016NativeProbeStopsAndJoinsWhenItsDiagnosticByteGrantIsExceeded()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await ScaleServerResourceBoundsFlow.RunAsync(diagnostic: true, TestContext.Current!.Execution.CancellationToken);
    }
}
