using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceBoundsTests
{
    private const string UnboundedOutputProcess = "/usr/bin/yes";

    [Test]
    public async Task AcScale016NativeProbeStopsAndJoinsWhenItsByteGrantIsExceeded()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var budget = UnitAppHostResourceOptions.Budget(64);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ScaleServerResourceProcess.RunAsync(
            UnboundedOutputProcess, [], cancellationToken: TestContext.Current!.Execution.CancellationToken, budget: budget));
    }
}
