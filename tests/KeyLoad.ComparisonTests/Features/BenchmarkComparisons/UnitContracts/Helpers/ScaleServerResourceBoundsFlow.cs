using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ScaleServerResourceBoundsFlow
{
    private const int SampleByteGrant = 64;
    private const string DirectoryPrefix = "keyload-scale-pipe-";
    private const string GuidFormat = "N";
    private const string StartGateName = "start-gate";

    internal static async Task RunAsync(bool diagnostic, CancellationToken testToken)
    {
        var budget = UnitAppHostResourceOptions.Budget(SampleByteGrant);
        using var timeout = new CancellationTokenSource(budget.Settings.CleanupThreshold, budget.TimeProvider);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(testToken, timeout.Token);
        var state = new State();
        var failures = new NativeSerializationBenchmarkFailures();
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
                () => ExecuteAsync(state, diagnostic, budget, cancellation.Token, timeout), failures.Add);
        }
        finally
        {
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(cancellation.CancelAsync, failures.Add);
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
                () => ScaleServerResourceBoundsNativeFixture.StopChildAsync(state.Child), failures.Add);
            await ObserveAsync(state.Original, state.ExpectedFailure, failures);
            await ObserveAsync(state.Healthy, expected: null, failures);
            IsolatedAggregateNodeGuardedInvocation.Capture(
                () => ScaleServerResourceBoundsNativeFixture.DeleteOwnedDirectory(state.Directory), failures.Add);
        }

        failures.ThrowIfAny();
    }

    private static async Task ExecuteAsync(State state, bool diagnostic, ScaleServerResourceSampleBudget budget,
        CancellationToken token, CancellationTokenSource timeout)
    {
        Directory.CreateDirectory(state.Directory);
        var original = ScaleServerResourceProcess.RunAsync(ScaleServerResourceBoundsNativeFixture.ShellProcess,
            ScaleServerResourceBoundsNativeFixture.Arguments(state.Marker, state.Gate, diagnostic), budget, token);
        state.Original = original;
        state.Child = await ScaleServerResourceCancellationNativeFixture.WaitForStartedChildAsync(state.Marker, budget, token);
        await Assert.That(ScaleServerResourceCancellationNativeFixture.IsOriginalChildRunning(state.Child.Value)).IsTrue();
        await File.WriteAllTextAsync(state.Gate, string.Empty, token);
        state.ExpectedFailure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => original);
        var expected = diagnostic ? ScaleServerResourceBoundsNativeFixture.ErrorBoundMessage : ScaleServerResourceBoundsNativeFixture.OutputBoundMessage;
        await Assert.That(state.ExpectedFailure?.Message).IsEqualTo(expected);
        await Assert.That(original.IsFaulted).IsTrue();
        await Assert.That(timeout.IsCancellationRequested).IsFalse();
        await Assert.That(ScaleServerResourceCancellationNativeFixture.IsOriginalChildRunning(state.Child.Value)).IsFalse();
        await HealthyAsync(state, token);
    }

    private static async Task HealthyAsync(State state, CancellationToken token)
    {
        var healthy = ScaleServerResourceProcess.RunAsync(ScaleServerResourceBoundsNativeFixture.HealthyProcess,
            [ScaleServerResourceBoundsNativeFixture.HealthyOutput], UnitAppHostResourceOptions.Budget(SampleByteGrant), token);
        state.Healthy = healthy;
        await Assert.That(await healthy).IsEqualTo(ScaleServerResourceBoundsNativeFixture.HealthyOutput);
        await Assert.That(healthy.IsCompletedSuccessfully).IsTrue();
    }

    private static async Task ObserveAsync(Task? original, Exception? expected, NativeSerializationBenchmarkFailures failures)
    {
        if (original is null)
        {
            return;
        }

        await original.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (original.Exception is { } envelope)
        {
            foreach (var failure in envelope.InnerExceptions)
            {
                if (!ReferenceEquals(failure, expected))
                {
                    failures.Add(failure);
                }
            }
        }
        if (original.IsCanceled)
        {
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(() => original, failures.Add);
        }
    }

    private sealed class State
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        internal string Marker => Path.Combine(Directory, ScaleServerResourceCancellationNativeFixture.StartMarkerName);
        internal string Gate => Path.Combine(Directory, StartGateName);
        internal ScaleServerResourceChildIdentity? Child { get; set; }
        internal Task<string?>? Original { get; set; }
        internal Task<string?>? Healthy { get; set; }
        internal Exception? ExpectedFailure { get; set; }
    }
}
