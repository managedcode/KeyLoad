using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorOfferPhaseOwner : IDisposable
{
    private const int Version = 1;
    internal EventVectorControlPhase Phase
    {
        get;
    }

    private EventVectorOfferPhaseOwner(EventVectorControlPhase phase) => Phase = phase;

    internal static EventVectorOfferPhaseOwner Create(EventFeedControlRequest request,
        DecodedGrainRequest parent, EventVectorPreparedOffer prepared,
        ImmutableArray<EventSourcePage> pages, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var entries = EventVectorOfferPageProjection.Create(prepared.Entries, pages, admission);
        var encodedEntries = ReadOnlyMemory<byte>.Empty;
        var encodedPages = ReadOnlyMemory<byte>.Empty;
        var checksum = ReadOnlyMemory<byte>.Empty;
        var transferred = false;
        try
        {
            encodedEntries = Encode(entries, admission, work);
            encodedPages = Encode(pages, admission, work);
            work.ChargeBytes(SHA256.HashSizeInBytes);
            checksum = SHA256.HashData(encodedEntries.Span);
            var phase = new EventVectorControlPhase(Version, request, EventVectorControlAction.Offer,
                prepared.Revision, prepared.CoverageGeneration, prepared.CleanupGeneration,
                null, null, null, encodedEntries, checksum)
            {
                OriginalCoverageWitness = prepared.OriginalCoverageWitness,
                OriginalParentExpiresAt = parent.Envelope.ExpiresAt,
                OriginalParentRequestId = parent.Envelope.RequestId,
                OriginalSourcePageBytes = encodedPages
            };
            EventVectorControlFields.Require(phase, admission);
            EventVectorOfferPageProof.Require(phase, prepared.Entries, entries, admission);
            work.Check();
            var result = new EventVectorOfferPhaseOwner(phase);
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                Clear(encodedEntries);
                Clear(encodedPages);
                Clear(checksum);
            }
        }
    }

    private static ReadOnlyMemory<byte> Encode<T>(T value, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var length = NativeSerialization.Measure(value);
        admission.RequireEncodedBytes(length);
        work.ChargeBytes(length);
        var bytes = NativeSerialization.Serialize(value);
        if (bytes.Length == length)
        {
            return bytes;
        }
        CryptographicOperations.ZeroMemory(bytes);
        throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
    }

    public void Dispose()
    {
        Clear(Phase.OriginalEncodedEntries);
        Clear(Phase.OriginalEntryChecksum);
        Clear(Phase.OriginalSourcePageBytes);
    }

    private static void Clear(ReadOnlyMemory<byte> bytes)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
}
