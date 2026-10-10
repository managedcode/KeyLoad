using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorControlPhaseIds
{
    private const int Version = 1;
    private const int IdentityBytes = 16;
    private const string InvalidIdentity = "The original event vector control identity is inconsistent.";

    internal static Guid For(EventVectorControlPhase phase, string principalId,
        ReadOnlyMemory<byte> originalStableBodyDigest, EventVectorAdmissionPolicy admission)
    {
        var actualBodyDigest = EventVectorControlBodyProjection.Digest(phase, admission);
        if (!CryptographicOperations.FixedTimeEquals(actualBodyDigest.Span, originalStableBodyDigest.Span))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidIdentity); }
        var identity = Project(phase, principalId, actualBodyDigest);
        admission.RequireEncodedBytes(NativeSerialization.Measure(identity));
        var encoded = NativeSerialization.Serialize(identity);
        admission.RequireEncodedBytes(encoded.Length);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(encoded, digest);
        return new Guid(digest[..IdentityBytes]);
    }

    internal static EventVectorControlIdentity Project(EventVectorControlPhase phase, string principalId,
        EventVectorAdmissionPolicy admission)
        => Project(phase, principalId, EventVectorControlBodyProjection.Digest(phase, admission));

    private static EventVectorControlIdentity Project(EventVectorControlPhase phase, string principalId,
        ReadOnlyMemory<byte> originalStableBodyDigest)
    {
        if (string.IsNullOrWhiteSpace(principalId) || originalStableBodyDigest.Length != SHA256.HashSizeInBytes)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidIdentity); }
        var source = phase.IntendedSourcePhase ?? phase.ObservedSourcePhase;
        if (source is not null && source.PrincipalId != principalId)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidIdentity); }
        return new EventVectorControlIdentity(Version, phase.OriginalRequest.CommandId,
            phase.OriginalRequest.MapId, EventVectorIdentityInputs.Partition(phase.OriginalRequest.ControlPartition),
            EventVectorIdentityInputs.Text(principalId), phase.Action,
            phase.ExpectedMapRevision, phase.ExpectedCoverageGeneration, phase.ExpectedCleanupGeneration,
            source?.PhaseCommandId ?? Guid.Empty, EventVectorIdentityInputs.Digest(originalStableBodyDigest),
            EventVectorIdentityInputs.Digest(phase.ObservedSourcePhase?.SourceOriginalOutcomeDigest
                ?? ReadOnlyMemory<byte>.Empty));
    }
}
