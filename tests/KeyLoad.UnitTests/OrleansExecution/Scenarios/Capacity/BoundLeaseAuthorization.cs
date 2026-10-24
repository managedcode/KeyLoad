using KeyLoad.Query.QueryExecution;
using K = KeyLoad.UnitTests.OrleansExecution.BoundLeaseProtocol;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class BoundLeaseAuthorization
{
    internal static async Task ReceivingTenantAsync(BoundLeaseSeeded state,
        BoundLeaseOperation operation, PartitionQueryOwnedLeafRequest original)
    {
        await Assert.That(original.Principal.ClusterAdministrator).IsTrue();
        await Assert.That(original.Principal.TenantId).IsNotEqualTo(K.Partition.TenantId);
        await Assert.That(original.Tenant).IsEqualTo(K.Partition.TenantId);
        var changed = original with
        {
            Tenant = original.Principal.TenantId
        };
        await ReadRefusedAsync(state, operation, changed, ErrorCode.Validation,
            "The partition query plan is invalid.");
        changed = original with
        {
            Principal = original.Principal with
            {
                TenantId = K.Partition.TenantId
            }
        };
        await ReadRefusedAsync(state, operation, changed, ErrorCode.PermissionDenied,
            "The partition query receiving identity changed.");
    }

    private static async Task ReadRefusedAsync(BoundLeaseSeeded state,
        BoundLeaseOperation operation, PartitionQueryOwnedLeafRequest request, ErrorCode code, string detail)
    {
        var before = operation.Ledger.RetainedEncodedBytes;
        var entries = operation.Ledger.RetainedEntries;
        PartitionQueryLeafResult? published = null;
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => published =
            BoundLeaseExecution.Read(state, operation, request))
            ?? throw new InvalidOperationException(K.Missing);
        await Assert.That(refused.Code).IsEqualTo(code);
        await Assert.That(refused.Message).IsEqualTo(detail);
        await Assert.That(published).IsNull();
        await BoundLeaseSeed.UnchangedAsync(state.Leader, state.Images[state.Leader]);
        await BoundLeaseSeed.UnchangedAsync(state.Other, state.Images[state.Other]);
        await Assert.That(operation.Ledger.RetainedEncodedBytes).IsEqualTo(before);
        await Assert.That(operation.Ledger.RetainedEntries).IsEqualTo(entries);
    }
}
