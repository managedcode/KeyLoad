using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.QueryExecution;

/// <summary>Owns only the two original native byte buffers for one addressed query exchange.</summary>
internal static class RemoteQueryLifetimeExchange
{
    internal static long Measure(RemoteDocumentCallV1 original, PartitionQueryLifetimeMetadata metadata,
        long maximumReplyBytes)
    {
        var payload = Encode(original, metadata);
        try
        {
            return NativeSerialization.Measure(Call(original, payload, maximumReplyBytes));
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }

    internal static async Task<T> ReadAsync<T>(RemoteDocumentClient client, RemoteDocumentCallV1 original,
        PartitionQueryLifetimeMetadata metadata, long maximumReplyBytes, CancellationToken token) where T : class
    {
        var payload = Encode(original, metadata);
        ReadOnlyMemory<byte> reply = default;
        var failures = new List<Exception>();
        T? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            reply = await client.ReadLifetimeAsync(Call(original, payload, maximumReplyBytes), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            result = NativeSerialization.Deserialize<T>(reply.Span)
                ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => Clear(reply), failures);
        ServerFailureObserver.Observe(() => CryptographicOperations.ZeroMemory(payload), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    private static byte[] Encode(RemoteDocumentCallV1 original, PartitionQueryLifetimeMetadata metadata)
    {
        var request = new RemoteQueryLifetimeRequestV2(original, metadata.Definition, metadata.Coverage, metadata.Principal);
        if (NativeSerialization.Measure(request) > RemoteDocumentProtocol.MaximumBodyBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.InvalidProof);
        }
        return NativeSerialization.Serialize(request);
    }

    private static RemoteResourceLifetimeReadCallV2 Call(RemoteDocumentCallV1 original,
        ReadOnlyMemory<byte> payload, long maximumReplyBytes)
        => new(RemoteResourceLifetimeReadProtocol.Version,
            original.QueryLeaf is not null ? RemoteResourceLifetimeReadKindV2.PartitionQuery
                : RemoteResourceLifetimeReadKindV2.DistributedSearch,
            original.RequestId, original.Nonce, original.ExpiresAt, original.CallerVoter,
            original.CallerSiloAddress, original.Source, original.Fence.Destination, payload,
            checked((int)Math.Min(maximumReplyBytes, RemoteDocumentProtocol.MaximumBodyBytes)));

    private static void Clear(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }
        if (!MemoryMarshal.TryGetArray(bytes, out var owned))
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        }
        CryptographicOperations.ZeroMemory(owned.AsSpan());
    }
}
