using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.BlobStorage;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobCommitCutProcess
{
    private const string UnsettledReaders = "The original blob process readers did not settle; process and root were retained.";
    private const string OriginalReadersKey = "BlobCommitCutOriginalReaders";
    private const string OriginalProcessKey = "BlobCommitCutOriginalProcess";
    internal static async Task RunAsync(OperationKind kind, BlobCommitCutKind cut,
        Func<string, BlobCommitCutKind, CancellationToken, Task> verify, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90), TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, deadline.Token);
        var root = Path.Combine(Path.GetTempPath(), "keyload-blob-commit-cut-" + Guid.NewGuid().ToString("N"));
        Process? process = null;
        Task<string>? errorDrain = null;
        Task<string>? outputDrain = null;
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            process = Start(root, kind, cut);
            errorDrain = ReadBoundedAsync(process.StandardError, linked.Token);
            await AwaitMarkerAsync(process, cut, errorDrain, linked.Token);
            outputDrain = ReadBoundedAsync(process.StandardOutput, linked.Token);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(linked.Token);
            _ = await Task.WhenAll(errorDrain, outputDrain);
            await KilledProcessFileReadiness.WaitAsync(root, linked.Token);
            RecoveryFileInventory.AssertNativeHandlesReleased(root);
            await verify(root, cut, linked.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30), TimeProvider.System);
            var failures = new List<Exception>();
            if (activeFailure is not null)
            {
                await ServerFailureObserver.ObserveAsync(linked.CancelAsync, failures);
            }
            await CleanupAsync(process, root, errorDrain, outputDrain, activeFailure, failures, cleanup.Token);
        }
    }

    private static Process Start(string root, OperationKind kind, BlobCommitCutKind cut)
    {
        var mode = kind switch
        {
            OperationKind.CompleteBlobUpload => BlobCommitCutProtocol.CompleteMode,
            OperationKind.DeleteBlob => BlobCommitCutProtocol.DeleteMode,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, cut.ToString(), "0", mode })
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("The genuine blob CrashHost could not start.");
    }

    private static async Task AwaitMarkerAsync(Process process, BlobCommitCutKind cut,
        Task<string> error, CancellationToken cancellationToken)
    {
        var marker = await process.StandardOutput.ReadLineAsync(cancellationToken);
        var expected = cut switch
        {
            BlobCommitCutKind.HeaderWritten or BlobCommitCutKind.JournalFlushed => CrashFixtureValues.CrashMarker,
            BlobCommitCutKind.UnknownAfterJournalFlush => BlobCommitCutProtocol.UnknownMarker,
            BlobCommitCutKind.Acknowledged => CrashFixtureValues.Acknowledgement,
            _ => throw new ArgumentOutOfRangeException(nameof(cut))
        };
        if (marker == expected)
        {
            return;
        }
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
        }
        throw new InvalidOperationException("The genuine blob process missed its selected native boundary: "
            + await error.WaitAsync(cancellationToken));
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var bytes = new char[8_192];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await reader.ReadAsync(bytes.AsMemory(count), cancellationToken);
            if (read == 0)
            {
                return new string(bytes, 0, count);
            }
            count += read;
        }
        throw new InvalidOperationException("The owned blob child exceeded its bounded pipe output.");
    }

    private static async Task CleanupAsync(Process? process, string root, Task<string>? error, Task<string>? output,
        Exception? primary, List<Exception> failures, CancellationToken token)
    {
        var originals = new[] { error, output }.OfType<Task<string>>().ToArray();
        var joined = await SettleDrainsAsync(process, originals, failures, token);
        if (joined)
        {
            var retention = primary ?? failures.FirstOrDefault();
            await ServerFailureObserver.ObserveAsync(() => RecoveryProcessCleanup.SettleAsync(
                process, root, root, retention, token), failures);
        }
        else
        {
            var unknown = new IOException(UnsettledReaders);
            unknown.Data[OriginalReadersKey] = originals;
            unknown.Data[OriginalProcessKey] = process;
            failures.Add(unknown);
        }
        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }

    private static async Task<bool> SettleDrainsAsync(Process? process, Task<string>[] originals,
        List<Exception> failures, CancellationToken token)
    {
        if (process is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(token);
                }
            }, failures);
        }
        var joined = Task.WhenAll(originals);
        await ServerFailureObserver.ObserveAsync(() => joined.WaitAsync(token), failures);
        foreach (var original in originals)
        {
            if (original.Exception is { } fault)
            {
                foreach (var failure in fault.InnerExceptions)
                {
                    CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
                }
            }
            else if (original.IsCanceled)
            {
                await ServerFailureObserver.ObserveAsync(() => original, failures);
            }
        }
        return originals.All(original => original.IsCompleted);
    }
}
