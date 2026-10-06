using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ScaleServerResourceCancellationFlow
{
    private const string NodeExecutable = "node";
    private const string HealthyOutput = "scale-native-healthy-follow-up";
    private const string TemporaryDirectoryPrefix = "keyload-scale-probe-cancel-";
    private const string GuidFormat = "N";
    private const string OwnedDirectoryChangedMessage = "The owned native probe directory changed before cleanup.";

    internal static async Task RunAsync(CancellationToken testToken)
    {
        var failures = new NativeSerializationBenchmarkFailures();
        var flow = new State(testToken);
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(async () =>
            {
                System.IO.Directory.CreateDirectory(flow.Directory);
                await ExecuteCompleteOperationAsync(flow, testToken);
            }, failures.Add);
        }
        finally
        {
            await CaptureSettlementAsync(flow.Original, flow.Cancellation, flow.OriginalCancellation, failures);
            await CaptureSettlementAsync(flow.Healthy, flow.HealthyCancellation, expectedCancellation: null, failures);
            IsolatedAggregateNodeGuardedInvocation.Capture(() => DeleteOwnedDirectory(flow), failures.Add);
            flow.Dispose(failures);
        }

        failures.ThrowIfAny();
    }

    private static async Task ExecuteCompleteOperationAsync(State flow, CancellationToken testToken)
    {
        var budget = UnitAppHostResourceOptions.Budget();
        await CancelOriginalOperationAsync(flow, budget, testToken);
        await ExecuteHealthyFollowUpAsync(flow, testToken);
    }

    private static async Task CancelOriginalOperationAsync(State flow, ScaleServerResourceSampleBudget budget,
        CancellationToken testToken)
    {
        var originalProbe = ScaleServerResourceProcess.RunAsync(NodeExecutable,
            ScaleServerResourceCancellationNativeFixture.StartArguments(flow.MarkerPath, budget.Settings.Cadence),
            budget, flow.Cancellation.Token);
        flow.Original = originalProbe;
        var child = await ScaleServerResourceCancellationNativeFixture.WaitForStartedChildAsync(flow.MarkerPath,
            budget, testToken);
        await flow.Cancellation.CancelAsync();
        flow.OriginalCancellation = await Assert.ThrowsAsync<OperationCanceledException>(() => originalProbe)
            ?? throw new InvalidOperationException(ScaleServerResourceCancellationNativeFixture.CancellationMissingMessage);
        await Assert.That(flow.OriginalCancellation.CancellationToken).IsEqualTo(flow.Cancellation.Token);
        await Assert.That(originalProbe.IsCanceled).IsTrue();
        await Assert.That(ScaleServerResourceCancellationNativeFixture.IsOriginalChildRunning(child)).IsFalse();
    }

    private static async Task ExecuteHealthyFollowUpAsync(State flow, CancellationToken testToken)
    {
        var healthyProbe = ScaleServerResourceProcess.RunAsync(NodeExecutable,
            ScaleServerResourceCancellationNativeFixture.HealthyArguments(HealthyOutput),
            UnitAppHostResourceOptions.Budget(), flow.HealthyCancellation.Token);
        flow.Healthy = healthyProbe;
        var output = await healthyProbe.WaitAsync(testToken);
        await Assert.That(output).IsEqualTo(HealthyOutput);
        await Assert.That(healthyProbe.IsCompletedSuccessfully).IsTrue();
    }

    private static Task CaptureSettlementAsync(Task<string?>? operation, CancellationTokenSource cancellation,
        OperationCanceledException? expectedCancellation, NativeSerializationBenchmarkFailures failures)
        => IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
            () => ScaleServerResourceCancellationSettlement.SettleAsync(operation, cancellation,
                expectedCancellation, failures), failures.Add);

    private static void DeleteOwnedDirectory(State flow)
    {
        if (flow.Original?.IsCompleted == false || flow.Healthy?.IsCompleted == false)
        {
            return;
        }

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(flow.Directory);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(OwnedDirectoryChangedMessage);
        }
        Directory.Delete(flow.Directory, recursive: true);
    }

    private sealed class State
    {
        internal string Directory { get; }
        internal string MarkerPath => Path.Combine(Directory, ScaleServerResourceCancellationNativeFixture.StartMarkerName);
        internal CancellationTokenSource Cancellation { get; }
        internal CancellationTokenSource HealthyCancellation { get; }
        internal Task<string?>? Original { get; set; }
        internal Task<string?>? Healthy { get; set; }
        internal OperationCanceledException? OriginalCancellation { get; set; }

        internal State(CancellationToken testToken)
        {
            Directory = Path.Combine(Path.GetTempPath(), TemporaryDirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
            Cancellation = new CancellationTokenSource();
            HealthyCancellation = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        }

        public void Dispose(NativeSerializationBenchmarkFailures failures)
        {
            DisposeSource(Cancellation, failures);
            DisposeSource(HealthyCancellation, failures);
        }

        private static void DisposeSource(CancellationTokenSource source, NativeSerializationBenchmarkFailures failures)
            => IsolatedAggregateNodeGuardedInvocation.Capture(source.Dispose, failures.Add);

    }
}
