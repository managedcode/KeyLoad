using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2InventoryRecoveryRefusals
{
    private const int MinimalBytes = 1;
    private const int MinimalRecords = 1;
    private const string ReaderId = "d2-inventory-reader";

    internal static async Task CheckAsync(ControlledPartitionMovementNativeNode source, CancellationToken token)
    {
        var principal = new PrincipalRecord(ReaderId, D2InventoryCrashProtocol.Tenant, [], []);
        var configure = source.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            D2InventoryCrashProtocol.Principal, source.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        _ = source.Journal.Submit(configure, token).Get<PrincipalRecord>();
        var before = D2InventoryRecoveryPages.Image(source);
        var deniedWork = D2InventoryRecoveryPages.Work(source, token);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => source.Database.AdmitResourceOwnerInventoryRequest(
            ReaderId, Guid.NewGuid(), D2InventoryCrashProtocol.Tenant, D2InventoryCrashProtocol.Database,
            D2InventoryCrashProtocol.ResourceA, D2InventoryCrashProtocol.FirstPageSize,
            source.Database.EvaluationClock.GetUtcNow().AddSeconds(source.Database.Limits.QueryDeadlineSeconds), deniedWork));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        var work = D2InventoryRecoveryPages.Work(source, token);
        var request = source.Database.AdmitResourceOwnerInventoryRequest(D2InventoryCrashProtocol.Principal,
            Guid.NewGuid(), D2InventoryCrashProtocol.Tenant, D2InventoryCrashProtocol.Database,
            D2InventoryCrashProtocol.ResourceA, D2InventoryCrashProtocol.FirstPageSize,
            source.Database.EvaluationClock.GetUtcNow().AddSeconds(source.Database.Limits.QueryDeadlineSeconds), work);
        var control = source.Database.CaptureResourceOwnerInventoryControl(D2InventoryCrashProtocol.Principal, request, work);
        var bounded = D2InventoryRecoveryPages.Work(source, token);
        bounded.ConstrainNativeWork(MinimalBytes, MinimalRecords);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => source.Database.ReadResourceOwnerInventoryPage(
            D2InventoryCrashProtocol.Principal, control, D2InventoryCrashCorpus.Source, null, bounded));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancelledWork = D2InventoryRecoveryPages.Work(source, cancelled.Token);
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => source.Database.ReadResourceOwnerInventoryPage(
            D2InventoryCrashProtocol.Principal, control, D2InventoryCrashCorpus.Source, null, cancelledWork));
        source.Database.ValidateResourceOwnerInventoryControl(D2InventoryCrashProtocol.Principal, control, work);
        await Assert.That(D2InventoryRecoveryPages.Image(source).SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
    }
}
