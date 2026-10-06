using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNativeTeardownSuccessTests
{
    [Test]
    public async Task ActualCaptureStopAndWritersSettleBeforeTeardownReturns()
    {
        var directory = Directory.CreateTempSubdirectory("keyload-native-teardown-");
        var evidence = Path.Combine(directory.FullName, "evidence");
        var output = Path.Combine(directory.FullName, "output");
        var dataRoot = Path.Combine(directory.FullName, "absent-data");
        try
        {
            await using var fixture = await IsolatedNativeTeardownNativeSupport.CreateFixtureAsync(
                Path.Combine(evidence, "progress.json"));
            var stopping = fixture.Capture.StopAsync();
            var applicationStopping = fixture.Application.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
            await IsolatedNativeTeardownNativeSupport.CompleteAsync(fixture, output, evidence, dataRoot, null);
            await Assert.That(stopping.IsCompletedSuccessfully).IsTrue();
            await Assert.That(ReferenceEquals(stopping, fixture.Capture.StopAsync())).IsTrue();
            await Assert.That(applicationStopping.IsCancellationRequested).IsTrue();
            await Assert.That(File.Exists(Path.Combine(evidence, IsolatedNativeTeardownNativeSupport.RunnerLog))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(evidence, IsolatedNativeTeardownNativeSupport.NodeLog))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(evidence, IsolatedNativeTeardownNativeSupport.ReceiptFile))).IsTrue();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
