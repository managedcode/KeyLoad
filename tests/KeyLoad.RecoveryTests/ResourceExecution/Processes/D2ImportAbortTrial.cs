using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportAbortTrial
{
    private const string Prefix = "keyload-d2-import-abort-";
    private const string GuidFormat = "N";

    internal static Task RunAsync(bool unknownTargetOrigin, CancellationToken token)
        => RunCoreAsync(D2ImportAbortFlow.ExecuteAsync, unknownTargetOrigin, token);

    internal static Task RunBeforeFirstPageAsync(bool unknownTargetOrigin, CancellationToken token)
        => RunCoreAsync(D2ImportAbortBeforePageFlow.ExecuteAsync, unknownTargetOrigin, token);

    private static async Task RunCoreAsync(Func<D2ImportSession, bool, CancellationToken, Task> operation,
        bool unknownTargetOrigin, CancellationToken token)
    {
        using var admission = await StorageTrialLease.AcquireAsync(token);
        using var deadline = new CancellationTokenSource(RecoveryExecutionOptions.Replica().Value.CommandTimeout, TimeProvider.System);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var session = new D2ImportSession(root, failures);
            await ServerFailureObserver.ObserveAsync(
                () => operation(session, unknownTargetOrigin, original.Token), failures);
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
