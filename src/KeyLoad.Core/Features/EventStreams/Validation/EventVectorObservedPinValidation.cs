using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorObservedPinValidation
{
    private const int Version = 1;
    private const long RevisionStep = 1;
    private const long InitialPosition = 0;
    private const string InvalidResult = "The actual native event vector pin result is inconsistent.";

    internal static void Require(EventVectorSourcePhase phase, EventVectorSourceMutation request,
        EventVectorSourceResult result, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(result);
        var pin = result.Pin;
        if (result.Version != Version || result.CommandId != phase.PhaseCommandId || pin is null
            || pin.Version != Version || pin.MapId != request.MapId || pin.ControlPartition != request.ControlPartition
            || pin.ControlIncarnation != request.ControlIncarnation || pin.Source != request.Source
            || pin.PrincipalId != request.PrincipalId || pin.SourcePolicyEpoch != request.SourcePolicyEpoch
            || pin.SourceSchemaVersion != request.SourceSchemaVersion || pin.Released
            || pin.Position != request.Position || pin.CoverageGeneration != request.CoverageGeneration
            || pin.PinRevision != checked(request.ExpectedPinRevision + RevisionStep)
            || !CryptographicOperations.FixedTimeEquals(pin.LastObservedControlPhaseDigest.Span,
                phase.OriginalBodyDigest.Span) || result.Token is null
            || result.Token.Incarnation != phase.SourceOwner.Incarnation
            || result.Token.AtomicPartitionId != phase.Source.Partition.AtomicPartitionId
            || result.Token.OwnershipEpoch != phase.SourceOwner.PlacementEpoch
            || result.Token.Position <= InitialPosition || !Enum.IsDefined(result.Durability))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
        }
        if (phase.Role == EventVectorSourcePhaseRole.Pin)
        {
            if (pin.OriginalPinCommandId != phase.PhaseCommandId || pin.OriginalPinExpiresAt != phase.FirstExpiresAt
                || !CryptographicOperations.FixedTimeEquals(pin.OriginalPinBodyDigest.Span,
                    phase.OriginalBodyDigest.Span))
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
            }
        }
        else
        {
            var original = EventVectorOriginalPinBinding.Read(request, admission);
            EventVectorOriginalPinBinding.RequireRetained(request, original, pin);
        }
    }
}
