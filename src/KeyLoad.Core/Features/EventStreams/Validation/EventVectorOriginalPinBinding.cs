using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorOriginalPinBinding
{
    private const int Version = 1;
    private const long InitialRevision = 0;
    private const string InvalidIdentity = "The original native event vector Pin identity is inconsistent.";

    internal static EventVectorSourceMutation Read(EventVectorSourceMutation request,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(admission);
        admission.RequireEncodedBytes(request.OriginalPinNativeBody.Length);
        if (request.OriginalPinCommandId == Guid.Empty || request.OriginalPinNativeBody.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidIdentity); }
        var original = NativeSerialization.Deserialize<EventVectorSourceMutation>(request.OriginalPinNativeBody.Span);
        if (original.Version != Version || original.Role != EventVectorSourcePhaseRole.Pin
            || original.MapId != request.MapId || original.ControlPartition != request.ControlPartition
            || original.ControlIncarnation != request.ControlIncarnation || original.Source != request.Source
            || original.PrincipalId != request.PrincipalId || original.OriginalPinCommandId != Guid.Empty
            || !original.OriginalPinNativeBody.IsEmpty || original.ExpectedPinRevision != InitialRevision
            || original.ExpectedCoverageGeneration != InitialRevision)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidIdentity); }
        return original;
    }

    internal static void RequireRetained(EventVectorSourceMutation request,
        EventVectorSourceMutation original, EventVectorSourcePin current)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(current);
        if (current.Version != Version || current.MapId != original.MapId
            || current.ControlPartition != original.ControlPartition
            || current.ControlIncarnation != original.ControlIncarnation || current.Source != original.Source
            || current.PrincipalId != original.PrincipalId || current.SourcePolicyEpoch != original.SourcePolicyEpoch
            || current.SourceSchemaVersion != original.SourceSchemaVersion
            || current.OriginalPinCommandId != request.OriginalPinCommandId
            || current.OriginalPinExpiresAt != original.FirstExpiresAt || current.Position < original.Position
            || !CryptographicOperations.FixedTimeEquals(current.OriginalPinBodyDigest.Span,
                SHA256.HashData(request.OriginalPinNativeBody.Span)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidIdentity); }
    }
}
