using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorSourceReadPin
{
    private const int Version = 1;
    private const long InitialPinRevision = 0;
    private const string Invalid = "The original event vector source read pin is inconsistent.";

    internal static EventVectorSourceMutation Require(IKeyValueView view, ReadEventSourceRequest read,
        EventVectorCleanupPin carrier, PrincipalRecord principal, EventVectorAdmissionPolicy admission)
    {
        if (carrier is null || carrier.Version != Version || carrier.OriginalPinPhaseBytes.IsEmpty
            || !carrier.LastAdmittedAdvancePhaseBytes.IsEmpty || !carrier.AuthenticatedSourceObservationBytes.IsEmpty
            || carrier.CurrentCoverageGeneration is null || carrier.CurrentPinRevision is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        admission.RequireEncodedBytes(carrier.OriginalPinPhaseBytes.Length);
        var phase = NativeSerialization.Deserialize<EventVectorSourcePhase>(carrier.OriginalPinPhaseBytes.Span);
        var mutation = EventVectorSourceMutationValidation.ReadOriginal(phase, admission);
        if (phase.Role != EventVectorSourcePhaseRole.Pin || mutation.OriginalPinCommandId != Guid.Empty
            || !mutation.OriginalPinNativeBody.IsEmpty || mutation.ExpectedPinRevision != InitialPinRevision
            || mutation.ExpectedCoverageGeneration != InitialPinRevision
            || phase.Source != read.Source
            || phase.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
        }
        var pin = EventVectorRowStorage.Read<EventVectorSourcePin>(view,
            EventVectorKeys.Pin(read.Source, phase.MapId), admission)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        if (pin.Version != Version || pin.Released || pin.MapId != phase.MapId
            || pin.ControlPartition != phase.ControlPartition || pin.ControlIncarnation != phase.ControlIncarnation
            || pin.Source != read.Source || pin.PrincipalId != principal.Id
            || pin.SourcePolicyEpoch != principal.PolicyEpoch || pin.SourcePolicyEpoch != phase.SourcePolicyEpoch
            || pin.SourceSchemaVersion != phase.SourceSchemaVersion || pin.OriginalPinCommandId != phase.PhaseCommandId
            || pin.OriginalPinExpiresAt != phase.FirstExpiresAt
            || pin.CoverageGeneration != carrier.CurrentCoverageGeneration || pin.PinRevision != carrier.CurrentPinRevision
            || read.AfterPosition != pin.Position || pin.Position < mutation.Position
            || !CryptographicOperations.FixedTimeEquals(pin.OriginalPinBodyDigest.Span, phase.OriginalBodyDigest.Span))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
        }
        return mutation;
    }
}
