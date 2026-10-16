using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal sealed class D2InventoryProcessTrial(List<Exception> failures) : IAsyncDisposable
{
    private const string Prefix = "keyload-d2-inventory-process-";
    private const string GuidFormat = "N";
    private const string Executable = "dotnet";
    private readonly string root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));
    private Process? process;
    private Task<string?>? marker;
    private Task<string>? errors;
    private Task<string>? output;
    private Task? disposal;

    internal static async Task RunAsync(D2InventoryCrashCut cut,
        Func<string, D2InventoryCrashCut, CancellationToken, Task> verify, CancellationToken token)
    {
        using var admission = await StorageTrialLease.AcquireAsync(token);
        using var deadline = new CancellationTokenSource(RecoveryExecutionOptions.Replica().Value.CommandTimeout, TimeProvider.System);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var trial = new D2InventoryProcessTrial(failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await trial.ExecuteAsync(cut, original.Token);
                await verify(trial.root, cut, original.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(D2InventoryCrashCut cut, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var start = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location,
                     D2InventoryCrashProtocol.Mode, root, cut.ToString() })
        {
            start.ArgumentList.Add(argument);
        }
        process = Process.Start(start) ?? throw new InvalidOperationException("The D2 native journal child did not start.");
        errors = D2InventoryProcessStreams.ReadAsync(process.StandardError);
        marker = process.StandardOutput.ReadLineAsync(token).AsTask();
        var line = await marker;
        var expected = cut == D2InventoryCrashCut.AppliedAcknowledged
            ? CrashFixtureValues.Acknowledgement : CrashFixtureValues.CrashMarker;
        if (line != expected || process.HasExited)
        {
            throw new InvalidOperationException("The D2 child did not reach its exact native durable boundary.");
        }
        output = D2InventoryProcessStreams.ReadAsync(process.StandardOutput);
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(token);
        await Task.WhenAll(errors, output).WaitAsync(token);
        if ((await errors).Length != 0 || (await output).Length != 0)
        {
            throw new InvalidOperationException("The original D2 child emitted unexpected bounded diagnostics.");
        }
        await D2RecoveryOwnerReadiness.RequirePairAsync(root, D2InventoryCrashProtocol.SourceDirectory,
            D2InventoryCrashProtocol.TargetDirectory, token);
        RecoveryFileInventory.AssertNativeHandlesReleased(root);
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        using var deadline = new CancellationTokenSource(RecoveryExecutionOptions.Replica().Value.CommandTimeout, TimeProvider.System);
        var cleanup = new List<Exception>();
        if (process is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                await process.WaitForExitAsync(deadline.Token);
            }, cleanup);
        }
        if (marker is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => marker.WaitAsync(deadline.Token), cleanup);
        }
        if (process is not null && output is null && marker?.IsCompleted != false)
        {
            ServerFailureObserver.Observe(() =>
            {
                output = D2InventoryProcessStreams.ReadAsync(process.StandardOutput);
            }, cleanup);
        }
        var joined = await D2InventoryProcessStreams.JoinAsync(process, marker, errors, output, cleanup, deadline.Token);
        if (joined && process is not null)
        {
            ServerFailureObserver.Observe(process.Dispose, cleanup);
        }
        if (joined && Directory.Exists(root))
        {
            await ServerFailureObserver.ObserveAsync(() => D2RecoveryOwnerReadiness.RequirePairAsync(root, D2InventoryCrashProtocol.SourceDirectory,
                D2InventoryCrashProtocol.TargetDirectory, deadline.Token), cleanup);
            ServerFailureObserver.Observe(() => RecoveryFileInventory.AssertNativeHandlesReleased(root), cleanup);
        }
        if (joined && failures.Count == 0 && cleanup.Count == 0 && Directory.Exists(root))
        {
            await ServerFailureObserver.ObserveAsync(() => StoragePublicationRecoveryTests.DeleteTrialAsync(root, deadline.Token), cleanup);
        }
        failures.AddRange(cleanup);
    }
}
