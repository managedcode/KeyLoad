using KeyLoad.Query.Features.QueryExecution;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeBoundLeaseProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseAuthorization
{
    internal static async Task ReceivingTenantAsync(NativeBoundLeaseSeeded state,
        NativeBoundLeaseOperation operation, PartitionQueryOwnedLeafRequestV2 original)
    {
        await Assert.That(original.Principal.ClusterAdministrator).IsTrue();
        await Assert.That(original.Principal.TenantId).IsNotEqualTo(K.Partition.TenantId);
        await Assert.That(original.Original.Tenant).IsEqualTo(K.Partition.TenantId);
        var changed = original with
        {
            Original = original.Original with
            {
                Tenant = original.Principal.TenantId
            }
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

    private static async Task ReadRefusedAsync(NativeBoundLeaseSeeded state,
        NativeBoundLeaseOperation operation, PartitionQueryOwnedLeafRequestV2 request, ErrorCode code, string detail)
    {
        var before = operation.Ledger.RetainedEncodedBytes;
        var entries = operation.Ledger.RetainedEntries;
        PartitionQueryLeafResultV1? published = null;
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => published =
            NativeBoundLeaseExecution.Read(state, operation, request))
            ?? throw new InvalidOperationException(K.Missing);
        await Assert.That(refused.Code).IsEqualTo(code);
        await Assert.That(refused.Message).IsEqualTo(detail);
        await Assert.That(published).IsNull();
        await NativeBoundLeaseSeed.UnchangedAsync(state.Leader, state.Images[state.Leader]);
        await NativeBoundLeaseSeed.UnchangedAsync(state.Other, state.Images[state.Other]);
        await Assert.That(operation.Ledger.RetainedEncodedBytes).IsEqualTo(before);
        await Assert.That(operation.Ledger.RetainedEntries).IsEqualTo(entries);
    }
}
