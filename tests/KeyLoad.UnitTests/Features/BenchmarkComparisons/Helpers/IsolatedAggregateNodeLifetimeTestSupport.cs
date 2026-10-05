using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeLifetimeTestSupport
{
    private const string TemporaryDirectoryPrefix = "keyload-node-owner-";
    private const string ReceiptFileName = "process-identities.json";
    private const string GuidFormat = "N";
    private const string ParentProperty = nameof(IsolatedAggregateNodeProcessReceipt.Parent);
    private const string ChildProperty = nameof(IsolatedAggregateNodeProcessReceipt.Child);
    private const int ReceiptPollMilliseconds = 10;
    private const int CancellationWaitMilliseconds = 500;

    internal static async Task CancelOwnedTreeAsync(TimeSpan testDeadline)
    {
        var directory = Path.Combine(Path.GetTempPath(), TemporaryDirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        var receipt = Path.Combine(directory, ReceiptFileName);
        var identities = new List<IsolatedAggregateNodeIdentity>();
        Exception? primary = null;
        var cleanupFailures = new List<Exception>();
        var timer = Stopwatch.StartNew();
        await using (var owner = new IsolatedAggregateNodePromptOwner(directory, identities, timer, cleanupFailures,
            TestContext.Current!.Execution.CancellationToken))
        {
            try
            {
                IsolatedAggregateNodeGuardedInvocation.Invoke(() => owner.Prompt.CancelAfter(testDeadline));
                owner.ExpectedCancellation = await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(
                    () => RunCancellationScenarioAsync(directory, receipt, owner.Prompt, identities, timer,
                        testDeadline, owner.RegisterOriginal, owner.RegisterCancellation, cleanupFailures));
            }
            catch (AggregateException envelope)
            {
                primary = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
            }
        }
        IsolatedAggregateNodeTestCleanup.ThrowFailures(primary, cleanupFailures);
    }

    private static async Task<IsolatedAggregateNodeProcessReceipt> ReadReceiptAsync(string path,
        CancellationToken cancellationToken)
    {
        while (!File.Exists(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(ReceiptPollMilliseconds, cancellationToken);
        }
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new(root.GetProperty(ParentProperty).GetInt32(), root.GetProperty(ChildProperty).GetInt32());
    }

    private static async Task<OperationCanceledException> RunCancellationScenarioAsync(string directory,
        string receipt, CancellationTokenSource prompt, List<IsolatedAggregateNodeIdentity> identities,
        Stopwatch timer, TimeSpan testDeadline, Action<Task<IsolatedAggregateNodeResult>> registerOriginal,
        Action<Task> registerCancellation, List<Exception> cancellationFailures)
    {
        Directory.CreateDirectory(directory);
        var started = IsolatedAggregateNodeProcess.RunAsync(
            ["-e", IsolatedAggregateNodeLifetimeProgram.Source,
                IsolatedAggregateNodeLifetimeProgram.CancellationTree, receipt], prompt.Token);
        registerOriginal(started);
        var receiptData = await ReadReceiptAsync(receipt, prompt.Token);
        CaptureIdentities(receiptData, identities);
        await Task.Delay(CancellationWaitMilliseconds, prompt.Token);
        var cancellationTask = prompt.CancelAsync();
        registerCancellation(cancellationTask);
        await ObserveCancellationWithinBoundAsync(cancellationTask, timer, testDeadline, cancellationFailures);
        var remaining = testDeadline - timer.Elapsed;
        var boundedObservation = started.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        var cancellation = await Assert.ThrowsAsync<OperationCanceledException>(() => boundedObservation)
            ?? throw new InvalidOperationException(IsolatedAggregateNodeLifetimeProgram.OriginalDidNotCancel);
        await Assert.That(prompt.IsCancellationRequested).IsTrue();
        await Assert.That(cancellation.CancellationToken.IsCancellationRequested).IsTrue();
        await Assert.That(timer.Elapsed < testDeadline).IsTrue();
        await AssertExitedAsync(identities, timer, testDeadline);
        return cancellation;
    }

    private static async Task ObserveCancellationWithinBoundAsync(Task cancellation, Stopwatch timer,
        TimeSpan testDeadline, List<Exception> failures)
    {
        var remaining = testDeadline - timer.Elapsed;
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(
                () => cancellation.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero));
        }
        catch (AggregateException envelope)
        {
            var failure = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
            if (failure is TimeoutException)
            {
                throw failure;
            }
            if (cancellation.IsCompleted)
            {
                IsolatedAggregateNodeTestCleanup.AddDistinct(failures, failure);
            }
            else
            {
                throw new TimeoutException(IsolatedAggregateNodeLifetimeProgram.OriginalDidNotSettle);
            }
        }
    }

    private static void CaptureIdentities(IsolatedAggregateNodeProcessReceipt receipt,
        List<IsolatedAggregateNodeIdentity> identities)
    {
        identities.Add(CaptureIdentity(receipt.Parent));
        identities.Add(CaptureIdentity(receipt.Child));
    }

    private static IsolatedAggregateNodeIdentity CaptureIdentity(int processId)
    {
        using var process = Process.GetProcessById(processId);
        if (process.HasExited)
        {
            throw new InvalidOperationException(IsolatedAggregateNodeLifetimeProgram.IdentityReceiptInvalid);
        }
        return new(processId, process.StartTime.ToUniversalTime().Ticks);
    }

    private static async Task AssertExitedAsync(List<IsolatedAggregateNodeIdentity> identities,
        Stopwatch timer, TimeSpan testDeadline)
    {
        while (timer.Elapsed < testDeadline && identities.Any(IsSameProcessRunning))
        {
            await Task.Delay(ReceiptPollMilliseconds);
        }
        await Assert.That(identities.All(identity => !IsSameProcessRunning(identity))).IsTrue();
    }

    private static bool IsSameProcessRunning(IsolatedAggregateNodeIdentity identity)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(identity.ProcessId);
        }
        catch (ArgumentException)
        {
            return false;
        }
        using (process)
        {
            return IsolatedAggregateNodeIdentityObservation.IsSameProcessRunning(process,
                identity.StartTimeUtcTicks);
        }
    }
}

internal sealed record IsolatedAggregateNodeProcessReceipt(int Parent, int Child);
internal sealed record IsolatedAggregateNodeIdentity(int ProcessId, long StartTimeUtcTicks);
