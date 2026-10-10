using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorCleanupFrontierIdentity
{
    private const int Version = 1;
    private const string Invalid = "The complete original cleanup frontier is inconsistent.";

    internal static ReadOnlyMemory<byte> Digest(ReadOnlyMemory<byte> originalBytes,
        EventVectorAdmissionPolicy admission)
    {
        if (originalBytes.IsEmpty)
        { return ReadOnlyMemory<byte>.Empty; }
        admission.RequireEncodedBytes(originalBytes.Length);
        var pins = NativeSerialization.Deserialize<ImmutableArray<EventVectorCleanupPin>>(originalBytes.Span);
        if (pins.IsDefault)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        admission.RequireEntryCount(pins.Length);
        var identities = ImmutableArray.CreateBuilder<EventVectorCleanupPinIdentity>(pins.Length);
        foreach (var pin in pins)
        {
            if (pin is null || pin.Version != Version || pin.OriginalPinPhaseBytes.IsEmpty
                || pin.AuthenticatedSourceObservationBytes.IsEmpty)
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            var original = NativeSerialization.Deserialize<EventVectorSourcePhase>(pin.OriginalPinPhaseBytes.Span);
            var observation = NativeSerialization.Deserialize<EventVectorSourceObservation>(
                pin.AuthenticatedSourceObservationBytes.Span);
            if (original.Role != EventVectorSourcePhaseRole.Pin || observation.Version != Version
                || observation.OriginalSourcePhaseCommandId != original.PhaseCommandId
                || observation.Source != original.Source || observation.PrincipalId != original.PrincipalId
                || !CryptographicOperations.FixedTimeEquals(observation.OriginalBodyDigest.Span,
                    original.OriginalBodyDigest.Span))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            RequireCurrent(pin, observation);
            identities.Add(new(EventVectorIdentityInputs.Digest(pin.OriginalPinPhaseBytes),
                EventVectorIdentityInputs.Digest(pin.LastAdmittedAdvancePhaseBytes),
                EventVectorIdentityInputs.Digest(observation.CurrentPinRowBytes),
                pin.CurrentCoverageGeneration, pin.CurrentPinRevision));
        }
        var projected = identities.ToImmutable();
        admission.RequireEncodedBytes(NativeSerialization.Measure(projected));
        var encoded = NativeSerialization.Serialize(projected);
        admission.RequireEncodedBytes(encoded.Length);
        return SHA256.HashData(encoded);
    }

    private static void RequireCurrent(EventVectorCleanupPin pin, EventVectorSourceObservation observation)
    {
        if (observation.CurrentPinRowBytes.IsEmpty)
        {
            if (pin.CurrentCoverageGeneration is not null || pin.CurrentPinRevision is not null)
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            return;
        }
        var current = NativeSerialization.Deserialize<EventVectorSourcePin>(observation.CurrentPinRowBytes.Span);
        if (current.CoverageGeneration != pin.CurrentCoverageGeneration || current.PinRevision != pin.CurrentPinRevision)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }
}
