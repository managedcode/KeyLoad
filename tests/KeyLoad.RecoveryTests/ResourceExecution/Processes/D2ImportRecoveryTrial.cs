using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportRecoveryTrial
{
    private const string RootPrefix = "keyload-d2-active-import-";
    private const string GuidFormat = "N";

    internal static async Task RunAsync(D2InventoryCrashCut cut, CancellationToken token)
    {
        using var admission = await StorageTrialLease.AcquireAsync(token);
        using var deadline = new CancellationTokenSource(RecoveryExecutionOptions.Replica().Value.CommandTimeout, TimeProvider.System);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var session = new D2ImportSession(root, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var process = new D2ImportProcess(root, failures);
                await ServerFailureObserver.ObserveAsync(
                    () => D2ImportRecoveryFlow.ExecuteAsync(session, process, cut, original.Token), failures);
            }, failures);
        }, failures);
        using var cleanup = new CancellationTokenSource(RecoveryExecutionOptions.Replica().Value.CommandTimeout, TimeProvider.System);
        if (failures.Count == D2ImportCrashProtocol.Empty && Directory.Exists(root))
        {
            await ServerFailureObserver.ObserveAsync(() => D2RecoveryOwnerReadiness.RequirePairAsync(root, D2ImportCrashProtocol.SourceDirectory,
                D2ImportCrashProtocol.TargetDirectory, cleanup.Token), failures);
            ServerFailureObserver.Observe(() => RecoveryFileInventory.AssertNativeHandlesReleased(root), failures);
        }
        if (failures.Count == D2ImportCrashProtocol.Empty && Directory.Exists(root))
        {
            await ServerFailureObserver.ObserveAsync(
                () => StoragePublicationRecoveryTests.DeleteTrialAsync(root, cleanup.Token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
