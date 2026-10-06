using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;

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
        await owner.CancelAsync();
        try
        { await original; }
        catch (OperationCanceledException failure) { return failure; }
        throw new InvalidOperationException("The actual Aspire runner wait did not observe caller cancellation.");
    }

    internal static Task CompleteAsync(ComparisonProgressNativeFixture fixture, string output,
        string evidence, string absentDataRoot, Exception? primary)
        => IsolatedNativeTeardown.CompleteAsync(fixture.Application, fixture.Capture, output, evidence,
            absentDataRoot, [fixture.Runner, fixture.Node],
            new IsolatedNativeOwnedWork(NativeExecutionPolicyFixture.Harness()), primary);

    internal static void CreateRawCopyConflict(string output, string evidence)
    {
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(output, WorkerFile), "source-marker");
        File.WriteAllText(Path.Combine(evidence, WorkerFile), "existing-marker");
    }

    internal static async Task CollectFailureAsync(Func<Task> operation, List<Exception> failures)
    {
        var initiation = StartAsync(operation);
        var failure = await OpenLoopFailure.ObserveAsync(initiation);
        if (failure is not null)
        {
            AddOriginalFailures(initiation, failure, failures);
            return;
        }
        var original = await initiation;
        failure = await OpenLoopFailure.ObserveAsync(original);
        if (failure is not null)
        { AddOriginalFailures(original, failure, failures); }
    }

    internal static void AddOriginalFailures(Task original, Exception observed, List<Exception> failures)
    {
        AddDistinct(observed, failures);
        if (original.Exception is { } aggregate)
        {
            foreach (var failure in aggregate.InnerExceptions)
            { AddDistinct(failure, failures); }
        }
    }

    private static void AddDistinct(Exception failure, List<Exception> failures)
    {
        if (!failures.Any(existing => ReferenceEquals(existing, failure)))
        { failures.Add(failure); }
    }

    private static async Task<Task> StartAsync(Func<Task> operation)
    {
        await Task.CompletedTask;
        return operation();
    }
}
