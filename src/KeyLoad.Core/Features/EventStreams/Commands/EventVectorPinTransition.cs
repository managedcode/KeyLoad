using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorPinTransition
{
    private const int Version = 1;
    private const long InitialExpectedRevision = 0;
    private const long FirstRevision = 1;
    private const long RevisionStep = 1;
    private const string InvalidTransition = "The native event vector pin transition is inconsistent.";
    private const string LostPin = "The original native event vector pin ownership is no longer available.";

    internal static EventVectorSourcePin Prepare(EventVectorSourceMutation request, Guid commandId,
        ReadOnlyMemory<byte> originalBody, EventSourceHead head, EventVectorSourcePin? current,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(admission);
        RequirePosition(request, head);
        return request.Role switch
        {
            EventVectorSourcePhaseRole.Pin => Create(request, commandId, originalBody, current),
            EventVectorSourcePhaseRole.Advance => Advance(request, originalBody, current, admission),
            _ => throw Errors.Fail(ErrorCode.Validation, InvalidTransition)
        };
    }

    private static EventVectorSourcePin Create(EventVectorSourceMutation request, Guid commandId,
        ReadOnlyMemory<byte> originalBody, EventVectorSourcePin? current)
    {
        if (current is not null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, LostPin); }
        if (request.ExpectedPinRevision != InitialExpectedRevision
            || request.ExpectedCoverageGeneration != InitialExpectedRevision || commandId == Guid.Empty
            || request.OriginalPinCommandId != Guid.Empty || !request.OriginalPinNativeBody.IsEmpty)
        { throw Errors.Fail(ErrorCode.Validation, InvalidTransition); }
        var digest = SHA256.HashData(originalBody.Span);
        return new EventVectorSourcePin
        {
            Version = Version,
            MapId = request.MapId,
            ControlPartition = request.ControlPartition,
            ControlIncarnation = request.ControlIncarnation,
            Source = request.Source,
            PrincipalId = request.PrincipalId,
            SourcePolicyEpoch = request.SourcePolicyEpoch,
            SourceSchemaVersion = request.SourceSchemaVersion,
            Position = request.Position,
            CoverageGeneration = request.CoverageGeneration,
            PinRevision = FirstRevision,
            OriginalPinCommandId = commandId,
            Released = false,
            LastObservedControlPhaseDigest = digest,
            OriginalPinExpiresAt = request.FirstExpiresAt,
            OriginalPinBodyDigest = digest
        };
    }

    private static EventVectorSourcePin Advance(EventVectorSourceMutation request,
        ReadOnlyMemory<byte> originalBody, EventVectorSourcePin? current, EventVectorAdmissionPolicy admission)
    {
        if (current is null || current.Released)
        { throw Errors.Fail(ErrorCode.OwnershipLost, LostPin); }
        var original = EventVectorOriginalPinBinding.Read(request, admission);
        EventVectorOriginalPinBinding.RequireRetained(request, original, current);
        if (current.Version != Version || current.MapId != request.MapId
            || current.ControlPartition != request.ControlPartition
            || current.ControlIncarnation != request.ControlIncarnation || current.Source != request.Source
            || current.PrincipalId != request.PrincipalId || current.SourcePolicyEpoch != request.SourcePolicyEpoch
            || current.SourceSchemaVersion != request.SourceSchemaVersion
            || current.CoverageGeneration != request.ExpectedCoverageGeneration
            || request.CoverageGeneration < current.CoverageGeneration
            || request.CoverageGeneration > checked(current.CoverageGeneration + RevisionStep)
            || current.PinRevision != request.ExpectedPinRevision || request.Position < current.Position)
        { throw Errors.Fail(ErrorCode.Conflict, InvalidTransition); }
        return current with
        {
            Position = request.Position,
            CoverageGeneration = request.CoverageGeneration,
            PinRevision = checked(current.PinRevision + RevisionStep),
            LastObservedControlPhaseDigest = SHA256.HashData(originalBody.Span)
        };
    }

    private static void RequirePosition(EventVectorSourceMutation request, EventSourceHead head)
    {
        if (head.Generation != request.Source.Generation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidTransition); }
        if (request.Position < checked(head.FirstAvailablePosition - FirstRevision))
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, InvalidTransition); }
        if (request.Position > head.TailPosition)
        { throw Errors.Fail(ErrorCode.Validation, InvalidTransition); }
    }
}
