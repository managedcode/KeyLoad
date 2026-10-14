using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphParentCall
{
    internal static async Task<T> ReadAsync<T>(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, GrainReadKind kind, object actualRequest, DateTimeOffset expiry,
        ReadExecutionBudget work, CancellationToken token)
    {
        var bytes = RemoteGraphNativeResultEncoding.Encode(actualRequest, partition.Database.Limits.MaxBatchBytes, work);
        try
        {
            var id = Guid.NewGuid();
            var signed = node.CatalogRequestCodec().RemoteGraph.CreateRemoteGraphRead(id, principal.Id, kind, bytes, expiry);
            var result = await RemoteGraphNativeChild.ExecuteAsync<T>(node, partition, principal,
                id, Guid.Empty, signed, false, null, work, token).ConfigureAwait(false);
            try
            {
                return Decode<T>(result, partition, work);
            }
            finally { Clear(result); }
        }
        finally { Clear(bytes); }
    }

    internal static async Task<T> CommandAsync<T>(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, ReplicatedOperation operation, DateTimeOffset expiry,
        RemoteGraphEffectBoundary boundary, ReadExecutionBudget work, CancellationToken token)
    {
        work.Check();
        token.ThrowIfCancellationRequested();
        var id = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().RemoteGraph.CreateRemoteGraphPhase(id, operation, expiry);
        var result = await RemoteGraphNativeChild.ExecuteAsync<T>(node, partition, principal,
            id, operation.Id, signed, true, boundary, work, token).ConfigureAwait(false);
        try
        {
            return Decode<T>(result, partition, work);
        }
        finally { Clear(result); }
    }

    internal static async Task<T> RemoteAsync<T>(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, RegisteredPhysicalOwnerV1 owner, RemoteGraphNativeAction action,
        object actualRequest, DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken token)
    {
        var bytes = RemoteGraphNativeResultEncoding.Encode(actualRequest, partition.Database.Limits.MaxBatchBytes, work);
        ReadOnlyMemory<byte> result = default;
        try
        {
            var reply = await node.BorrowRemoteGraphClient().SendAsync(owner, action, principal.Id,
                bytes, Guid.NewGuid(), expiry, work, token).ConfigureAwait(false);
            result = reply.Payload;
            return Decode<T>(result, partition, work);
        }
        finally
        {
            Clear(bytes);
            if (!result.IsEmpty && System.Runtime.InteropServices.MemoryMarshal.TryGetArray(result, out var array))
            {
                CryptographicOperations.ZeroMemory(array.AsSpan());
            }
        }
    }

    private static void Clear(ReadOnlyMemory<byte> bytes)
    {
        if (!bytes.IsEmpty && System.Runtime.InteropServices.MemoryMarshal.TryGetArray(bytes, out var array))
        {
            CryptographicOperations.ZeroMemory(array.AsSpan());
        }
    }

    private static T Decode<T>(ReadOnlyMemory<byte> bytes, PartitionHost partition, ReadExecutionBudget work)
    {
        work.Check();
        if (bytes.IsEmpty || bytes.Length > partition.Database.Limits.MaxBatchBytes
            || bytes.Length > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        work.ChargeBytes(bytes.Length);
        return NativeSerialization.Deserialize<T>(bytes.Span);
    }
}
