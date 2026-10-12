using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class NativeCanonicalModelCutCodec
{
    internal const int MaximumRecordBytes = 8192;
    private const int EmptyPayloadBytes = 0;
    private const long InitialStorePosition = 0;
    private const string InvalidDetail = "The private canonical cut does not match its bounded scope.";
    private const string ExhaustedDetail = "The private canonical cut exceeds its complete native envelope budget.";

    internal static byte[] Encode(NativeCanonicalModelCutWitness witness, CancellationToken token)
    {
        RequireEnvelope(witness, token);
        var bytes = NativeSerialization.Serialize(witness);
        token.ThrowIfCancellationRequested();
        if (bytes.Length > MaximumRecordBytes)
        { throw Exhausted(); }
        return bytes;
    }

    internal static void RequireEnvelope(NativeCanonicalModelCutWitness witness, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateWitness(witness);
        if (NativeSerialization.Measure(witness) > MaximumRecordBytes)
        { throw Exhausted(); }
        token.ThrowIfCancellationRequested();
    }

    internal static NativeCanonicalModelCutWitness Decode(ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (payload.Length is <= EmptyPayloadBytes or > MaximumRecordBytes)
        { throw Exhausted(); }
        var witness = NativeSerialization.Deserialize<NativeCanonicalModelCutWitness>(payload.ToArray())
            ?? throw Invalid();
        ValidateWitness(witness);
        token.ThrowIfCancellationRequested();
        return witness with
        {
            Rows = witness.Rows.Select(row => new NativeCanonicalModelCutRow(row.Key.ToArray(), row.Value.ToArray())).ToImmutableArray(),
            LogicalOutcome = Own(witness.LogicalOutcome), TechnicalOutcome = Own(witness.TechnicalOutcome),
            TargetReceipt = Own(witness.TargetReceipt), TargetCounter = Own(witness.TargetCounter),
            SourceIntent = Own(witness.SourceIntent), SourceCounter = Own(witness.SourceCounter)
        };
    }

    internal static void ValidateRequest(NativeCanonicalModelCutRequest request)
    {
        if (request is null || request.Nonce == Guid.Empty || request.ArmId == Guid.Empty
            || request.AcceptCommandId == Guid.Empty || request.TransferId == Guid.Empty
            || !Guid.TryParseExact(request.SessionId, RequestCqrsProbeProtocol.SessionIdFormat, out var session) || session == Guid.Empty
            || session.ToString(RequestCqrsProbeProtocol.SessionIdFormat) != request.SessionId || request.SourcePartition is null
            || request.DestinationPartition is null || request.SourcePartition == request.DestinationPartition
            || request.SourceQueue?.Partition != request.SourcePartition
            || request.DestinationQueue?.Partition != request.DestinationPartition)
        { throw Invalid(); }
        ValidatePartition(request.SourcePartition);
        ValidatePartition(request.DestinationPartition);
        JsonData.Identifier(request.SourceQueue.Queue);
        JsonData.Identifier(request.DestinationQueue.Queue);
        JsonData.Identifier(request.LogicalPrincipalId);
        JsonData.Identifier(request.TechnicalPrincipalId);
        if (NativeSerialization.Measure(request) > MaximumRecordBytes)
        { throw Exhausted(); }
    }

    private static void ValidateWitness(NativeCanonicalModelCutWitness witness)
    {
        if (witness is null || witness.Owner is null || witness.StorePosition < InitialStorePosition || witness.Rows.IsDefault
            || witness.Rows.Length > MaximumRecordBytes || witness.Owner.Incarnation == Guid.Empty
            || !witness.Owner.TransportReady)
        { throw Invalid(); }
        ValidateRequest(witness.Request);
        JsonData.Identifier(witness.Owner.VoterId);
        JsonData.Identifier(witness.Owner.ClusterId);
        if (string.IsNullOrWhiteSpace(witness.Owner.SiloAddress)
            || witness.Owner.SiloAddress.Length > ReplicaTransportProtocol.MaximumAddressCharacters
            || global::Orleans.Runtime.SiloAddress.FromParsableString(witness.Owner.SiloAddress).ToParsableString() != witness.Owner.SiloAddress)
        { throw Invalid(); }
        var prefixes = PartitionRecordFamilies.All.SelectMany(family => new[]
        { KeySpace.Partition(family, witness.Request.SourcePartition), KeySpace.Partition(family, witness.Request.DestinationPartition) }).ToArray();
        foreach (var row in witness.Rows)
        {
            if (row is null || row.Key.IsEmpty || row.Key.Length > MaximumRecordBytes || row.Value.Length > MaximumRecordBytes
                || !prefixes.Any(prefix => row.Key.Span.StartsWith(prefix)))
            { throw Invalid(); }
        }
        foreach (var bytes in new[] { witness.LogicalOutcome, witness.TechnicalOutcome, witness.TargetReceipt,
            witness.TargetCounter, witness.SourceIntent, witness.SourceCounter })
        {
            if (bytes is { } value && (value.IsEmpty || value.Length > MaximumRecordBytes))
            { throw Invalid(); }
        }
    }

    private static void ValidatePartition(PartitionRef partition)
    {
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
    }

    private static ReadOnlyMemory<byte>? Own(ReadOnlyMemory<byte>? bytes)
        => bytes is { } value ? new ReadOnlyMemory<byte>(value.ToArray()) : null;
    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, InvalidDetail);
    internal static KeyLoadException Exhausted() => Errors.Fail(ErrorCode.ResourceExhausted, ExhaustedDetail);
}
