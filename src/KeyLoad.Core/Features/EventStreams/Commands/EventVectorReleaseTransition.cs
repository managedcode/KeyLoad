using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorReleaseTransition
{
    private const int Version = 1;
    private const long InitialRevision = 0;
    private const long RevisionStep = 1;
    private const string InvalidRelease = "The original native event vector Pin release identity is inconsistent.";

    internal static EventVectorSourcePin Prepare(EventVectorSourceMutation request,
        EventVectorSourcePin? current, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(admission);
        admission.RequireEncodedBytes(request.OriginalPinNativeBody.Length);
        if (request.Role != EventVectorSourcePhaseRole.Release || request.OriginalPinCommandId == Guid.Empty
            || request.OriginalPinNativeBody.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidRelease); }
        var original = NativeSerialization.Deserialize<EventVectorSourceMutation>(request.OriginalPinNativeBody.Span);
        RequireOriginal(request, original);
        var digest = SHA256.HashData(request.OriginalPinNativeBody.Span);
        if (current is null)
        {
            if (request.ExpectedPinRevision != InitialRevision
                || request.Position != original.Position
                || request.ExpectedCoverageGeneration != original.CoverageGeneration
                || request.CoverageGeneration != original.CoverageGeneration)
            { throw Errors.Fail(ErrorCode.Conflict, InvalidRelease); }
            return new EventVectorSourcePin
            {
                Version = Version,
                MapId = original.MapId,
                ControlPartition = original.ControlPartition,
                ControlIncarnation = original.ControlIncarnation,
                Source = original.Source,
                PrincipalId = original.PrincipalId,
                SourcePolicyEpoch = original.SourcePolicyEpoch,
                SourceSchemaVersion = original.SourceSchemaVersion,
                Position = original.Position,
                CoverageGeneration = original.CoverageGeneration,
                PinRevision = RevisionStep,
                OriginalPinCommandId = request.OriginalPinCommandId,
                Released = true,
                OriginalPinExpiresAt = original.FirstExpiresAt,
                OriginalPinBodyDigest = digest
            };
        }
        RequireCurrent(request, original, current, digest);
        return current with { Released = true, PinRevision = checked(current.PinRevision + RevisionStep) };
    }

    private static void RequireOriginal(EventVectorSourceMutation request, EventVectorSourceMutation original)
    {
        if (original.Version != Version || original.Role != EventVectorSourcePhaseRole.Pin
            || original.MapId != request.MapId || original.ControlPartition != request.ControlPartition
            || original.ControlIncarnation != request.ControlIncarnation || original.Source != request.Source
            || original.PrincipalId != request.PrincipalId
            || original.OriginalPinCommandId != Guid.Empty || !original.OriginalPinNativeBody.IsEmpty
            || original.ExpectedPinRevision != InitialRevision
            || original.ExpectedCoverageGeneration != InitialRevision)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidRelease); }
    }

    private static void RequireCurrent(EventVectorSourceMutation request, EventVectorSourceMutation original,
        EventVectorSourcePin current, ReadOnlySpan<byte> digest)
    {
        if (current.Version != Version || current.MapId != original.MapId
            || current.ControlPartition != original.ControlPartition
            || current.ControlIncarnation != original.ControlIncarnation || current.Source != original.Source
            || current.PrincipalId != original.PrincipalId || current.SourcePolicyEpoch != original.SourcePolicyEpoch
            || current.SourceSchemaVersion != original.SourceSchemaVersion
            || current.CoverageGeneration != request.ExpectedCoverageGeneration
            || current.CoverageGeneration != request.CoverageGeneration
            || current.OriginalPinCommandId != request.OriginalPinCommandId
            || current.OriginalPinExpiresAt != original.FirstExpiresAt
            || !CryptographicOperations.FixedTimeEquals(current.OriginalPinBodyDigest.Span, digest))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidRelease); }
        if (current.PinRevision != request.ExpectedPinRevision || current.Position != request.Position)
        { throw Errors.Fail(ErrorCode.Conflict, InvalidRelease); }
    }
}
