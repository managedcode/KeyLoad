using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceCancellationTests
{
    private const string Sleep = "/bin/sleep";
    private const string Duration = "30";

    [Test]
    public async Task AcScale016CancellationStopsAndJoinsTheOwnedNativeProbe()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        var probe = ScaleServerResourceProcess.RunAsync(Sleep, [Duration], cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => probe);
    }
}
