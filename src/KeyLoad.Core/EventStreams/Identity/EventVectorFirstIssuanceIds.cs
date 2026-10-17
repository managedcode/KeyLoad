using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorFirstIssuanceIds
{
    private const int Version = 1;
    private const int IdentityBytes = 16;
    private const string Invalid = "The initial event vector intent identity is inconsistent.";

    internal static EventVectorFirstIssuanceIdentity Admit(EventFeedControlRequest originalRequest,
        string actualPrincipalId, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(originalRequest);
        ArgumentNullException.ThrowIfNull(admission);
        JsonData.Identifier(actualPrincipalId);
        return new(Version, EventVectorControlAction.AdmitFirstIntent,
            EventVectorIdentityInputs.Text(actualPrincipalId), originalRequest.CommandId,
            EventVectorIdentityInputs.Partition(originalRequest.ControlPartition),
            EventVectorIdentityInputs.Digest(EventVectorControlBodyProjection.RequestDigest(originalRequest, admission)),
            ReadOnlyMemory<byte>.Empty);
    }

    internal static Guid For(EventVectorFirstIssuanceIdentity originalIdentity,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(originalIdentity);
        ArgumentNullException.ThrowIfNull(admission);
        if (originalIdentity.Version != Version || originalIdentity.Action != EventVectorControlAction.AdmitFirstIntent
            || originalIdentity.OriginalPublicCommandId == Guid.Empty || originalIdentity.ControlPartition is null
            || string.IsNullOrWhiteSpace(originalIdentity.PrincipalId)
            || originalIdentity.OriginalPublicRequestDigest.Length != SHA256.HashSizeInBytes
            || !originalIdentity.OriginalCapturedFirstPhaseDigest.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        var identity = new EventVectorFirstIssuanceIdentity(originalIdentity.Version,
            originalIdentity.Action, EventVectorIdentityInputs.Text(originalIdentity.PrincipalId),
            originalIdentity.OriginalPublicCommandId,
            EventVectorIdentityInputs.Partition(originalIdentity.ControlPartition),
            EventVectorIdentityInputs.Digest(originalIdentity.OriginalPublicRequestDigest),
            EventVectorIdentityInputs.Digest(originalIdentity.OriginalCapturedFirstPhaseDigest));
        admission.RequireEncodedBytes(NativeSerialization.Measure(identity));
        var encoded = NativeSerialization.Serialize(identity);
        admission.RequireEncodedBytes(encoded.Length);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(encoded, digest);
        return new Guid(digest[..IdentityBytes]);
    }
}
