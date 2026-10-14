using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.BlobStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobCommitCutProcess
{
    private const string PipeCleanupDiagnosticKey = "BlobCommitCutPipeCleanup";
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
            try
            {
                await SettleDrainsAsync(process, errorDrain, outputDrain, activeFailure, cleanup.Token);
            }
            finally { await RecoveryProcessCleanup.SettleAsync(process, root, root, activeFailure, cleanup.Token); }
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

    private static async Task SettleDrainsAsync(Process? process, Task<string>? error, Task<string>? output,
        Exception? activeFailure, CancellationToken cancellationToken)
    {
        if (process is null)
        {
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            var pending = new List<Task<string>>();
            if (error is not null)
            {
                pending.Add(error);
            }
            if (output is not null)
            {
                pending.Add(output);
            }
            _ = await Task.WhenAll(pending).WaitAsync(cancellationToken);
        }
        catch (Exception failure) when (activeFailure is not null)
        {
            activeFailure.Data[PipeCleanupDiagnosticKey] = failure;
        }
    }
}
