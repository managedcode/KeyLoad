using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorOfferPinObservation : IDisposable
{
    private const int Version = 1;
    private readonly EventVectorSourceObservation original;
    private readonly EventVectorSourcePhase observed;
    internal EventVectorCleanupPin Carrier
    {
        get;
    }

    private EventVectorOfferPinObservation(EventVectorSourceObservation original,
        EventVectorSourcePhase observed, ReadOnlyMemory<byte> originalPinBytes, EventVectorSourcePin pin)
    {
        this.original = original;
        this.observed = observed;
        Carrier = new(Version, originalPinBytes, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
            pin.CoverageGeneration, pin.PinRevision);
    }

    internal static EventVectorOfferPinObservation Read(EventVectorSourcePhase originalPin,
        EventVectorEntry acknowledged, EventVectorRemoteCall actualCall, EventVectorMacCheckedReply authenticated,
        ReadOnlyMemory<byte> originalPinBytes, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var reply = authenticated.Reply;
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, EventVectorRemoteProtocol.Unavailable);
        }
        admission.RequireEncodedBytes(reply.OriginalNativeReply.Length);
        work.ChargeBytes(reply.OriginalNativeReply.Length);
        work.ChargeBytes(authenticated.OriginalReplyBytes.Length);
        work.ChargeBytes(SHA256.HashSizeInBytes);
        var original = NativeSerialization.Deserialize<EventVectorSourceObservation>(reply.OriginalNativeReply.Span);
        EventVectorSourcePhase? observed = null;
        var transferred = false;
        try
        {
            observed = EventVectorAuthenticatedSourceObservation.Read(originalPin, actualCall, authenticated, admission);
            admission.RequireEncodedBytes(original.CurrentPinRowBytes.Length);
            if (original.CurrentPinRowBytes.IsEmpty)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid);
            }
            var pin = NativeSerialization.Deserialize<EventVectorSourcePin>(original.CurrentPinRowBytes.Span);
            if (pin.Released || pin.Source != acknowledged.Source || pin.Position != acknowledged.Position
                || pin.MapId != originalPin.MapId || pin.PrincipalId != originalPin.PrincipalId
                || pin.CoverageGeneration != originalPin.CoverageGeneration)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, EventVectorRemoteProtocol.Invalid);
            }
            work.Check();
            var result = new EventVectorOfferPinObservation(original, observed, originalPinBytes, pin);
            transferred = true;
            return result;
        }
        finally { if (!transferred) { Clear(original, observed); } }
    }

    public void Dispose() => Clear(original, observed);

    private static void Clear(EventVectorSourceObservation original, EventVectorSourcePhase? observed)
    {
        Clear(original.OriginalStoredOutcome);
        Clear(original.CurrentPinRowBytes);
        Clear(original.OriginalBodyDigest);
        if (observed is not null)
        {
            Clear(observed.SourceObservationWitness);
            Clear(observed.SourceOriginalOutcomeDigest);
        }
    }

    private static void Clear(ReadOnlyMemory<byte> bytes)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
}
