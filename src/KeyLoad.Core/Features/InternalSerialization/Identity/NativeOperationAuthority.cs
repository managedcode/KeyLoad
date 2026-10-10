using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Copies and verifies an existing Core-issued native operation without normalizing public JSON.</summary>
    /// <param name="operation">Caller-owned envelope whose native bytes must not flow into later effects.</param>
    /// <returns>The verified envelope with a private owned native snapshot; retain this result for all later reads.</returns>
    public ReplicatedOperation VerifyOperationAuthority(ReplicatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RequireNativeBudget(operation.NativePayload.Length);
        if (operation.NativePayload.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, NativeCommandContract.MissingAuthority); }
        var owned = operation with { NativePayload = operation.NativePayload.ToArray() };
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(owned.NativePayload.Span);
        var identity = Store.Identity;
        var claims = ReadNativeAuthority(identity, payload);
        VerifyNativeAuthority(identity, claims, owned.Id, owned.Kind, owned.PrincipalId,
            NativeOperationFingerprint.Compute(owned), payload);
        return owned;
    }

    // Borrowed peer admission: caller holds its input stable until this synchronous check returns.
    // Only small metadata/authority is decoded; no JSON string or typed command body is materialized.
    internal void VerifyNativeAuthority(Guid id, OperationKind kind, string principalId, ReadOnlySpan<byte> payloadJsonUtf8,
        ReadOnlyMemory<byte> value, ErrorCode? error, string? safeDetail,
        ReadOnlyMemory<byte> authority, ReadOnlyMemory<byte> signature, ReadOnlyMemory<byte> retryDecisions = default, ReadOnlyMemory<byte> transferProof = default)
    {
        var payload = new NativeCommandPayload(value, error, safeDetail) { Authority = authority, Signature = signature, RetryDecisions = retryDecisions, TransferProof = transferProof };
        var identity = Store.Identity;
        var claims = ReadNativeAuthority(identity, payload);
        VerifyNativeAuthority(identity, claims, id, kind, principalId,
            NativeOperationFingerprint.Compute(id, kind, principalId, payloadJsonUtf8), payload);
    }

    private static void VerifyNativeAuthority(StoreIdentity identity, NativeCommandAuthority claims,
        Guid id, OperationKind kind, string principalId, string fingerprint, NativeCommandPayload payload)
    {
        Span<byte> valueHash = stackalloc byte[NativeAuthorityContract.DigestBytes];
        _ = SHA256.HashData(payload.Value.Span, valueHash);
        if (claims.Purpose != KeyLoad.Core.Features.Search.OnlineTextNativeAuthorityPurpose.For(kind) || claims.Incarnation != identity.Incarnation
            || claims.OperationId != id || claims.Kind != kind || claims.PrincipalId != principalId
            || claims.Fingerprint != fingerprint || claims.Error != payload.Error || claims.SafeDetail != payload.SafeDetail
            || claims.ValueHash.Length != valueHash.Length || !CryptographicOperations.FixedTimeEquals(claims.ValueHash.Span, valueHash)
            || !MatchesQueueRetryHash(payload.RetryDecisions.Span, claims.RetryDecisionHash.Span)
            || !MatchesRemoteTransferProofHash(payload.TransferProof.Span, claims.TransferProofHash.Span)
            || !ValidNativeMarker(payload))
        { throw Errors.Fail(ErrorCode.Corruption, NativeCommandContract.MismatchedAuthority); }
    }

    private void RequireNativeBudget(long byteCount)
    {
        if (byteCount > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidCommandBudgetMessage); }
    }

    private static NativeCommandAuthority ReadNativeAuthority(StoreIdentity identity, NativeCommandPayload payload)
    {
        if (payload.Authority.IsEmpty || payload.Signature.Length != NativeAuthorityContract.DigestBytes)
        { throw Errors.Fail(ErrorCode.Corruption, NativeCommandContract.MissingAuthority); }
        Span<byte> expected = stackalloc byte[NativeAuthorityContract.DigestBytes];
        _ = HMACSHA256.HashData(identity.SigningKey.Span, payload.Authority.Span, expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, payload.Signature.Span))
        { throw Errors.Fail(ErrorCode.Corruption, NativeCommandContract.InvalidSignature); }
        return NativeSerialization.Deserialize<NativeCommandAuthority>(payload.Authority.Span);
    }

    private ReplicatedOperation IssueNativeOperation(ReplicatedOperation operation, NativeCommandPayload payload)
    {
        var identity = Store.Identity;
        var claims = new NativeCommandAuthority(KeyLoad.Core.Features.Search.OnlineTextNativeAuthorityPurpose.For(operation.Kind), identity.Incarnation,
            operation.Id, operation.Kind, operation.PrincipalId, NativeOperationFingerprint.Compute(operation),
            SHA256.HashData(payload.Value.Span), payload.Error, payload.SafeDetail,
            payload.RetryDecisions.IsEmpty ? ReadOnlyMemory<byte>.Empty : SHA256.HashData(payload.RetryDecisions.Span),
            payload.TransferProof.IsEmpty ? ReadOnlyMemory<byte>.Empty : SHA256.HashData(payload.TransferProof.Span));
        var authority = NativeSerialization.Serialize(claims);
        var signed = payload with { Authority = authority, Signature = HMACSHA256.HashData(identity.SigningKey.Span, authority) };
        var native = NativeSerialization.Serialize(signed);
        RequireNativeBudget(native.Length);
        return operation with { NativePayload = native };
    }

    private static bool ValidNativeMarker(NativeCommandPayload payload) => payload.Error is null
        ? !payload.Value.IsEmpty && payload.SafeDetail is null
        : payload.Value.IsEmpty && payload.SafeDetail is not null
            && payload.Error is ErrorCode.Validation or ErrorCode.Corruption or ErrorCode.UnsupportedCapability;
}
