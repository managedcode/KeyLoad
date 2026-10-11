using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorFirstCaptureSet
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;

    internal static ImmutableArray<EventVectorEntry> Read(PhysicalOwnerDirectoryV1 directory,
        EventFeedControlRequest request, string principalId, EventVectorCapturedExchange[] captures,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(captures);
        admission.RequireEntryCount(captures.Length);
        if (directory.Owners.IsDefaultOrEmpty || captures.Length != directory.Owners.Length)
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Invalid); }
        var entries = ImmutableArray.CreateBuilder<EventVectorEntry>();
        var sources = new HashSet<EventSourceRef>();
        for (var ordinal = FirstOrdinal; ordinal < captures.Length; ordinal++)
        {
            var exchange = captures[ordinal]
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Invalid);
            var call = new EventVectorCoverageCall(Version, request, principalId, ordinal);
            var capture = EventVectorCoverageCaptureAdmission.Read(exchange.OriginalPacket,
                call, directory.Owners[ordinal], admission, work);
            try
            {
                EventVectorFirstCaptureDirectory.Require(directory, capture, admission, work);
                Add(entries, sources, capture, admission, work);
            }
            finally { EventVectorCaptureBuffers.Clear(capture); }
        }
        return entries.ToImmutable();
    }

    private static void Add(ImmutableArray<EventVectorEntry>.Builder entries,
        HashSet<EventSourceRef> sources, EventVectorCoverageCapture capture,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var original = EventVectorEntryEncoding.Decode(capture.OriginalEncodedEntries,
            capture.EntryChecksum, admission);
        admission.RequireEntryCount(checked(entries.Count + original.Length));
        foreach (var entry in original)
        {
            if (!sources.Add(entry.Source))
            { throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid); }
            work.ChargeBytes(NativeSerialization.Measure(entry));
            var source = entry.Source;
            entries.Add(entry with
            {
                Source = source with
                {
                    Partition = EventVectorIdentityInputs.Partition(source.Partition),
                    Resource = EventVectorIdentityInputs.Text(source.Resource),
                    StreamId = source.StreamId is null ? null : EventVectorIdentityInputs.Text(source.StreamId)
                },
                PrincipalId = EventVectorIdentityInputs.Text(entry.PrincipalId)
            });
        }
    }
}
