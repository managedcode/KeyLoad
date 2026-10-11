using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorOfferOwnedEncoding
{
    private const string Invalid = "The original event vector offer encoding is inconsistent.";

    internal static EventVectorPreparedOffer Create(EventVectorMap map, ImmutableArray<EventVectorEntry> entries,
        EventVectorCoverageWitness witness, ImmutableArray<EventVectorSourcePhase> pins,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var encodedWitness = ReadOnlyMemory<byte>.Empty;
        var encodedPins = ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>(pins.Length);
        var transferred = false;
        try
        {
            encodedWitness = Encode(witness, admission, work);
            foreach (var pin in pins)
            { encodedPins.Add(Encode(pin, admission, work)); }
            work.Check();
            var prepared = new EventVectorPreparedOffer(map, entries, encodedWitness, encodedPins.ToImmutable());
            transferred = true;
            return prepared;
        }
        finally
        {
            if (!transferred)
            {
                Clear(encodedWitness);
                foreach (var pin in encodedPins)
                { Clear(pin); }
            }
        }
    }

    internal static ReadOnlyMemory<byte> Encode<T>(T value, EventVectorAdmissionPolicy admission,
        ReadExecutionBudget work)
    {
        var length = NativeSerialization.Measure(value);
        admission.RequireEncodedBytes(length);
        work.ChargeBytes(length);
        var encoded = NativeSerialization.Serialize(value);
        if (encoded.Length == length)
        { return encoded; }
        Clear(encoded);
        throw Errors.Fail(ErrorCode.Corruption, Invalid);
    }

    private static void Clear(ReadOnlyMemory<byte> bytes)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
}
