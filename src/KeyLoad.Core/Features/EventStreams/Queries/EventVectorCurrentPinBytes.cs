using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorCurrentPinBytes
{
    private const int Version = 1;
    private const string Invalid = "The current event vector source pin does not match its original identity.";

    internal static ReadOnlyMemory<byte> Read(IKeyValueView view, EventVectorSourcePhase phase,
        EventVectorSourceMutation request, EventVectorAdmissionPolicy admission)
    {
        var key = EventVectorKeys.Pin(request.Source, request.MapId);
        var original = ReadOnlyMemory<byte>.Empty;
        view.ReadValue(key, bytes =>
        {
            admission.RequireEncodedBytes(checked(key.Length + bytes.Length));
            var pin = NativeSerialization.Deserialize<EventVectorSourcePin>(bytes);
            if (phase.Role == EventVectorSourcePhaseRole.Pin)
            { RequirePin(pin, phase, request); }
            else
            {
                var first = EventVectorOriginalPinBinding.Read(request, admission);
                EventVectorOriginalPinBinding.RequireRetained(request, first, pin);
            }
            original = bytes.ToArray();
        });
        return original;
    }

    private static void RequirePin(EventVectorSourcePin pin, EventVectorSourcePhase phase,
        EventVectorSourceMutation request)
    {
        if (pin.Version != Version || pin.MapId != request.MapId || pin.ControlPartition != request.ControlPartition
            || pin.ControlIncarnation != request.ControlIncarnation || pin.Source != request.Source
            || pin.PrincipalId != request.PrincipalId || pin.SourcePolicyEpoch != request.SourcePolicyEpoch
            || pin.SourceSchemaVersion != request.SourceSchemaVersion
            || pin.OriginalPinCommandId != phase.PhaseCommandId || pin.OriginalPinExpiresAt != phase.FirstExpiresAt
            || !CryptographicOperations.FixedTimeEquals(pin.OriginalPinBodyDigest.Span, phase.OriginalBodyDigest.Span))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }
}
