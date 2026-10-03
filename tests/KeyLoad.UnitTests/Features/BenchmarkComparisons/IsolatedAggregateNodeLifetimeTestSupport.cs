using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeLifetimeTestSupport
{
    private const string TemporaryDirectoryPrefix = "keyload-node-owner-";
    private const string ReceiptFileName = "process-identities.json";
    private const string GuidFormat = "N";
    private const int ReceiptPollMilliseconds = 10;
    private const int CancellationWaitMilliseconds = 500;

    internal static async Task CancelOwnedTreeAsync(TimeSpan testDeadline)
    {
        var directory = Path.Combine(Path.GetTempPath(), TemporaryDirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        var receipt = Path.Combine(directory, ReceiptFileName);
        using var prompt = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        prompt.CancelAfter(testDeadline);
        Task<IsolatedAggregateNodeResult>? original = null;
        OperationCanceledException? expectedCancellation = null;
        var identities = new List<IsolatedAggregateNodeIdentity>();
        Exception? primary = null;
        var cleanupFailures = new List<Exception>();
        var timer = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(directory);
            var started = IsolatedAggregateNodeProcess.RunAsync(
                ["-e", IsolatedAggregateNodeLifetimeProgram.Source,
                    IsolatedAggregateNodeLifetimeProgram.CancellationTree, receipt], prompt.Token);
            original = started;
            var receiptData = await ReadReceiptAsync(receipt, prompt.Token);
            CaptureIdentities(receiptData, identities);
            await Task.Delay(CancellationWaitMilliseconds, prompt.Token);
            prompt.Cancel();
            expectedCancellation = await Assert.ThrowsAsync<OperationCanceledException>(() => started)
                ?? throw new InvalidOperationException(IsolatedAggregateNodeLifetimeProgram.OriginalDidNotCancel);
            await Assert.That(prompt.IsCancellationRequested).IsTrue();
            await Assert.That(expectedCancellation.CancellationToken.IsCancellationRequested).IsTrue();
            await Assert.That(timer.Elapsed < testDeadline).IsTrue();
            await AssertExitedAsync(identities, timer, testDeadline);
        }
        catch (Exception failure)
        {
            primary = failure;
        }
        finally
        {
            await IsolatedAggregateNodeTestCleanup.CleanupAsync(prompt, original, expectedCancellation,
                directory, identities, timer, cleanupFailures);
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
        return JsonSerializer.Deserialize<IsolatedAggregateNodeProcessReceipt>(json)
            ?? throw new InvalidDataException(IsolatedAggregateNodeLifetimeProgram.IdentityReceiptInvalid);
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
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != identity.StartTimeUtcTicks)
            {
                return false;
            }
            return !process.HasExited;
        }
    }
}

internal sealed record IsolatedAggregateNodeProcessReceipt(int Parent, int Child);
internal sealed record IsolatedAggregateNodeIdentity(int ProcessId, long StartTimeUtcTicks);
