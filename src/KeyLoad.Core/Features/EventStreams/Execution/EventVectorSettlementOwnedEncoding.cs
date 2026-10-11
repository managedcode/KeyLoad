using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorSettlementOwnedEncoding
{
    internal static EventVectorPreparedSettlement Create(EventVectorMap map, ImmutableArray<EventVectorEntry> entries,
        EventVectorCoverageWitness witness, ImmutableArray<EventVectorSourcePhase> pins,
        ImmutableArray<EventVectorEntry> targets, ReadOnlyMemory<byte> targetBytes, ReadOnlyMemory<byte> targetDigest,
        ImmutableArray<long> currentSourcePolicyEpochs, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var encodedWitness = ReadOnlyMemory<byte>.Empty;
        var encodedPins = ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>(pins.Length);
        var transferred = false;
        try
        {
            encodedWitness = EventVectorOfferOwnedEncoding.Encode(witness, admission, work);
            foreach (var pin in pins)
            { encodedPins.Add(EventVectorOfferOwnedEncoding.Encode(pin, admission, work)); }
            work.Check();
            var result = new EventVectorPreparedSettlement(map, entries, encodedWitness, encodedPins.ToImmutable(),
                targets, targetBytes, targetDigest, currentSourcePolicyEpochs);
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                EventVectorSettlementTargetEncoding.Clear(encodedWitness);
                foreach (var pin in encodedPins)
                { EventVectorSettlementTargetEncoding.Clear(pin); }
            }
        }
    }
}
