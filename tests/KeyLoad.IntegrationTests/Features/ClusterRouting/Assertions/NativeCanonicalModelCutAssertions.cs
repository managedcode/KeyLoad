using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutAssertions
{
    private const string Invalid = "The authenticated native cut differs from its exact original scope or canonical bytes.";

    internal static void Require(NativeCanonicalModelCutRequest original, ReplicaSiloDiscovery actualOwner,
        NativeCanonicalModelCutWitness returned, CancellationToken token)
    {
        NativeCanonicalModelCutCodec.RequireEnvelope(returned, token);
        RequireBytes(NativeSerialization.Serialize(original), NativeSerialization.Serialize(returned.Request));
        RequireBytes(NativeSerialization.Serialize(actualOwner), NativeSerialization.Serialize(returned.Owner));
        var rows = Rows(returned, token);
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, original.DestinationPartition);
        Selected(rows, CommandOutcomeKeyResolver.ForNew(original.LogicalPrincipalId, original.AcceptCommandId, scope).Key,
            returned.LogicalOutcome);
        Selected(rows, CommandOutcomeKeyResolver.ForNew(original.TechnicalPrincipalId, original.AcceptCommandId, scope).Key,
            returned.TechnicalOutcome);
        Selected(rows, RemoteTransferStorage.TargetReceiptKey(original.SourceQueue, original.TransferId,
            original.DestinationQueue), returned.TargetReceipt);
        Selected(rows, RemoteTransferStorage.TargetCapacityKey(original.DestinationQueue), returned.TargetCounter);
        Selected(rows, RemoteTransferStorage.IntentKey(original.SourceQueue, original.TransferId), returned.SourceIntent);
        Selected(rows, RemoteTransferStorage.SourceCapacityKey(original.SourceQueue), returned.SourceCounter);
        token.ThrowIfCancellationRequested();
    }

    internal static void RequireUnchanged(NativeCanonicalModelCutWitness original,
        NativeCanonicalModelCutWitness returned, CancellationToken token)
    {
        NativeCanonicalModelCutCodec.RequireEnvelope(original, token);
        NativeCanonicalModelCutCodec.RequireEnvelope(returned, token);
        RequireBytes(NativeSerialization.Serialize(original.Request with
        {
            Nonce = returned.Request.Nonce
        }),
            NativeSerialization.Serialize(returned.Request));
        var before = Rows(original, token);
        var after = Rows(returned, token);
        if (before.Count != after.Count)
        {
            throw new InvalidDataException(Invalid);
        }
        foreach (var row in before)
        {
            token.ThrowIfCancellationRequested();
            if (!after.TryGetValue(row.Key, out var actual))
            {
                throw new InvalidDataException(Invalid);
            }
            RequireBytes(row.Value.Span, actual.Span);
        }
        RequireNullable(original.LogicalOutcome, returned.LogicalOutcome);
        RequireNullable(original.TechnicalOutcome, returned.TechnicalOutcome);
        RequireNullable(original.TargetReceipt, returned.TargetReceipt);
        RequireNullable(original.TargetCounter, returned.TargetCounter);
        RequireNullable(original.SourceIntent, returned.SourceIntent);
        RequireNullable(original.SourceCounter, returned.SourceCounter);
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> Rows(NativeCanonicalModelCutWitness witness,
        CancellationToken token)
    {
        var rows = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var row in witness.Rows)
        {
            token.ThrowIfCancellationRequested();
            if (!rows.TryAdd(Convert.ToHexString(row.Key.Span), row.Value))
            {
                throw new InvalidDataException(Invalid);
            }
        }
        return rows;
    }

    private static void Selected(Dictionary<string, ReadOnlyMemory<byte>> rows, byte[] key,
        ReadOnlyMemory<byte>? selected)
    {
        var present = rows.TryGetValue(Convert.ToHexString(key), out var raw);
        if (present != selected.HasValue)
        {
            throw new InvalidDataException(Invalid);
        }
        if (selected is { } exact)
        {
            RequireBytes(raw.Span, exact.Span);
        }
    }

    private static void RequireNullable(ReadOnlyMemory<byte>? original, ReadOnlyMemory<byte>? returned)
    {
        if (original.HasValue != returned.HasValue)
        {
            throw new InvalidDataException(Invalid);
        }
        if (original is { } before && returned is { } after)
        {
            RequireBytes(before.Span, after.Span);
        }
    }

    private static void RequireBytes(ReadOnlySpan<byte> original, ReadOnlySpan<byte> returned)
    {
        if (!original.SequenceEqual(returned))
        {
            throw new InvalidDataException(Invalid);
        }
    }
}
