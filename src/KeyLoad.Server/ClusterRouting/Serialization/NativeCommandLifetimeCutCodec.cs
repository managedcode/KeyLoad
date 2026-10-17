using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class NativeCommandLifetimeCutCodec
{
    private const long InitialCount = 0;
    private const int DigestCharacters = 64;
    private const int EmptyBytes = 0;

    internal static void Validate(NativeCommandLifetimeCutRequest request)
    {
        if (request is null || request.Nonce == Guid.Empty || request.ArmId == Guid.Empty
            || request.CommandId == Guid.Empty || request.EffectCommandId == Guid.Empty || request.Partition is null
            || !Guid.TryParseExact(request.SessionId, RequestCqrsProbeProtocol.SessionIdFormat, out var session)
            || session == Guid.Empty || session.ToString(RequestCqrsProbeProtocol.SessionIdFormat) != request.SessionId)
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        JsonData.Identifier(request.PrincipalId);
        JsonData.Identifier(request.TechnicalPrincipalId);
        JsonData.Identifier(request.Partition.TenantId);
        JsonData.Identifier(request.Partition.DatabaseId);
        JsonData.Identifier(request.Partition.TransactionDomainId);
        JsonData.Identifier(request.Partition.PartitionKey);
        if (NativeSerialization.Measure(request) > NativeCommandLifetimeCutWire.MaximumRecordBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
    }

    internal static byte[] Encode(NativeCommandLifetimeCutWitness witness, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateWitness(witness);
        if (NativeSerialization.Measure(witness) > NativeCommandLifetimeCutWire.MaximumRecordBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
        var result = NativeSerialization.Serialize(witness);
        token.ThrowIfCancellationRequested();
        return result;
    }

    internal static NativeCommandLifetimeCutWitness Decode(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (bytes.Length is <= EmptyBytes or > NativeCommandLifetimeCutWire.MaximumRecordBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
        var result = NativeSerialization.Deserialize<NativeCommandLifetimeCutWitness>(bytes.Span);
        ValidateWitness(result);
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static void ValidateWitness(NativeCommandLifetimeCutWitness witness)
    {
        if (witness is null || witness.Owner is null || !witness.Owner.TransportReady
            || witness.Owner.Incarnation == Guid.Empty || witness.StorePosition < InitialCount
            || witness.ModelRowCount < InitialCount || witness.ModelReadBytes < InitialCount
            || witness.ModelDigest is not { Length: DigestCharacters } || !witness.ModelDigest.All(char.IsAsciiHexDigitLower))
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        Validate(witness.Request);
        JsonData.Identifier(witness.Owner.ClusterId);
        JsonData.Identifier(witness.Owner.VoterId);
        if (string.IsNullOrWhiteSpace(witness.Owner.SiloAddress)
            || witness.Owner.SiloAddress.Length > ReplicaTransportProtocol.MaximumAddressCharacters
            || global::Orleans.Runtime.SiloAddress.FromParsableString(witness.Owner.SiloAddress).ToParsableString() != witness.Owner.SiloAddress)
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        foreach (var outcome in new[] { witness.LogicalOutcome, witness.TechnicalOutcome })
        {
            if (outcome is { } value && (value.IsEmpty || value.Length > NativeCommandLifetimeCutWire.MaximumRecordBytes))
            {
                throw NativeCanonicalModelCutCodec.Exhausted();
            }
        }
    }
}
