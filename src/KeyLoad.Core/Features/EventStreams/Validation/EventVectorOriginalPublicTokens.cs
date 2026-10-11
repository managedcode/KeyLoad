using System.Security.Cryptography;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidOriginalEventVectorPublicToken = "The original public event vector token requires recovery.";

    private void RequireEventVectorOriginalPublicTokens(ReadOnlyMemory<byte> phaseBytes,
        ReadOnlyMemory<byte> outcomeBytes, PrincipalRecord principal)
    {
        if (phaseBytes.IsEmpty || outcomeBytes.IsEmpty)
        { return; }
        var phase = NativeSerialization.Deserialize<EventVectorControlPhase>(phaseBytes.Span);
        var outcome = NativeSerialization.Deserialize<StoredOutcome>(outcomeBytes.Span);
        if (outcome.Result.Error is not null)
        { return; }
        if (EventVectorFirstCleanupResultValidation.Handles(phase.Action))
        {
            _ = EventVectorFirstCleanupResultValidation.Read(outcome.Result.Get<EventVectorFirstIssuanceResult>(),
                phase, principal.Id, Store.Identity.Incarnation, new EventVectorAdmissionPolicy(OperationLimitsOptions));
            return;
        }
        var result = outcome.Result.Get<EventVectorControlResult>();
        var value = EventVectorOriginalPublicResult.Read(result, phase,
            new EventVectorAdmissionPolicy(OperationLimitsOptions));
        if (value is null || value.Released)
        { return; }
        var cursor = VerifyOriginalEventVectorPublicToken(value.Cursor!);
        RequireEventVectorOriginalPublicToken(cursor, phase, principal, value, EventVectorMapTokenPurpose);
        var expires = result.Map?.ExpiresAt ?? result.Offer?.ExpiresAt
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidOriginalEventVectorPublicToken);
        if (cursor.ExpiresAt != expires)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidOriginalEventVectorPublicToken); }
        var digest = result.Map?.SourceManifestDigest
            ?? VerifyOriginalEventVectorPublicToken(phase.OriginalRequest.Cursor!).ManifestDigest;
        if (!CryptographicOperations.FixedTimeEquals(cursor.ManifestDigest.Span, digest.Span))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidOriginalEventVectorPublicToken); }
        if (result.Offer is not { } offer)
        { return; }
        var token = VerifyOriginalEventVectorPublicToken(value.OfferDigest!);
        RequireEventVectorOriginalPublicToken(token, phase, principal, value, EventVectorOfferTokenPurpose);
        if (token.ExpiresAt != cursor.ExpiresAt
            || !CryptographicOperations.FixedTimeEquals(token.ManifestDigest.Span, offer.PageDigest.Span))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidOriginalEventVectorPublicToken); }
    }

    private EventVectorToken VerifyOriginalEventVectorPublicToken(string value)
    {
        try
        { return Verify<EventVectorToken>(value); }
        catch (KeyLoadException error) when (error.Code == ErrorCode.TokenInvalidated
            && ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidOriginalEventVectorPublicToken, error); }
    }

    private void RequireEventVectorOriginalPublicToken(EventVectorToken token, EventVectorControlPhase phase,
        PrincipalRecord principal, EventFeedControlResult value, string purpose)
    {
        if (token is null || token.Version != EventVectorTokenVersion || token.Purpose != purpose
            || token.MapId != value.MapId || token.ControlPartition != phase.OriginalRequest.ControlPartition
            || token.MapRevision != value.Revision || token.CoverageGeneration != value.CoverageGeneration
            || token.PrincipalId != principal.Id || token.ControlPolicyEpoch != principal.PolicyEpoch
            || token.ControlIncarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidOriginalEventVectorPublicToken); }
        if (token.ExpiresAt <= EvaluationClock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.CursorExpired, InvalidOriginalEventVectorPublicToken); }
    }
}
