using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeTeardownNativeSupport
{
    internal const string WorkerFile = "worker.json";
    internal const string RunnerLog = "runner.log";
    internal const string NodeLog = "progress-native-node.log";
    internal const string ReceiptFile = "teardown.json";

    internal static Task<ComparisonProgressNativeFixture> CreateFixtureAsync(string progressPath)
        => ComparisonProgressNativeFixture.CreateAsync(progressPath);

    internal static async Task<OperationCanceledException> CancelNativeRunnerWaitAsync(
        ComparisonProgressNativeFixture fixture, CancellationTokenSource owner)
    {
        var original = AspireResourceCompletion.WaitForExitAsync(fixture.Application, IsolatedResourceTopologyFixture.RunnerName, owner.Token);
        owner.Cancel();
        try { await original; }
        catch (OperationCanceledException failure) { return failure; }
        throw new InvalidOperationException("The actual Aspire runner wait did not observe caller cancellation.");
    }

    internal static Task CompleteAsync(ComparisonProgressNativeFixture fixture, string output,
        string evidence, string absentDataRoot, Exception? primary)
        => IsolatedNativeTeardown.CompleteAsync(fixture.Application, fixture.Capture, output, evidence,
            absentDataRoot, [fixture.Runner, fixture.Node], new IsolatedNativeOwnedWork(), primary);

    internal static void CreateRawCopyConflict(string output, string evidence)
    {
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(output, WorkerFile), "source-marker");
        File.WriteAllText(Path.Combine(evidence, WorkerFile), "existing-marker");
    }
}
