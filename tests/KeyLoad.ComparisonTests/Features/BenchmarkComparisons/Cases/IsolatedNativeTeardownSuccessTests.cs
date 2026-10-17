using KeyLoad.UnitTests.Features.BenchmarkComparisons;
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
            Directory.CreateDirectory(evidence);
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

    [Test]
    public async Task OwnedCaptureCannotBeReplacedAndSettlesItsOriginalWritersBeforeReturn()
    {
        using var directory = new ImageBundleTestDirectory();
        var evidence = Path.Combine(directory.Root, "evidence");
        var output = Path.Combine(directory.Root, "output");
        var data = Path.Combine(directory.Root, "absent-data");
        Directory.CreateDirectory(evidence);
        await using var fixture = await IsolatedNativeTeardownNativeSupport.CreateFixtureAsync(
            Path.Combine(directory.Root, "fixture-progress.json"));
        var work = new IsolatedNativeOwnedWork(NativeExecutionPolicyFixture.Harness());
        Task original = Task.CompletedTask;
        Exception? bodyFailure = null;
        try
        {
            work.BeginCapture(fixture.Application, [fixture.Node.Name], Path.Combine(evidence, "progress.json"));
            var capture = work.Capture!;
            Assert.ThrowsExactly<InvalidOperationException>(() => work.BeginCapture(fixture.Application,
                [fixture.Node.Name], Path.Combine(evidence, "replacement-progress.json")));
            await Assert.That(ReferenceEquals(capture, work.Capture)).IsTrue();
            original = IsolatedNativeTeardown.CompleteAsync(fixture.Application, output, evidence, data,
                [fixture.Runner, fixture.Node], work, null);
            await original;
            await Assert.That(capture.StopAsync().IsCompletedSuccessfully).IsTrue();
            await Assert.That(File.Exists(Path.Combine(evidence, IsolatedNativeTeardownNativeSupport.RunnerLog))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(evidence, IsolatedNativeTeardownNativeSupport.NodeLog))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(evidence, IsolatedNativeTeardownNativeSupport.ReceiptFile))).IsTrue();
            var completion = work.DisposeAsync().AsTask();
            await completion;
            await Assert.That(ReferenceEquals(completion, work.DisposeAsync().AsTask())).IsTrue();
            await Assert.That(capture.DisposeAsync().IsCompletedSuccessfully).IsTrue();
            Assert.ThrowsExactly<InvalidOperationException>(() => work.BeginCapture(fixture.Application,
                [fixture.Node.Name], Path.Combine(evidence, "after-close-progress.json")));
        }
        catch (Exception failure)
        {
            bodyFailure = failure;
            throw;
        }
        finally
        {
            await IsolatedNativeTeardown.SettleOwnerDisposalAsync(
                bodyFailure is null ? original : Task.FromException(bodyFailure), work.DisposeAsync().AsTask());
        }
    }
}
