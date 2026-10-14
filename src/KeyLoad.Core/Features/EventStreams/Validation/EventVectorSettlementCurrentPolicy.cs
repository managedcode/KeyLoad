using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

internal static class EventVectorSettlementCurrentPolicy
{
    private const int FirstOrdinal = 0;
    private const long EmptyPolicyEpoch = 0;
    private const string Invalid = "The current event vector settlement source policy is inconsistent.";

    internal static ImmutableArray<long> Read(EventFeedControlRequest request,
        ImmutableArray<EventVectorEntry> entries, ImmutableArray<EventVectorSourcePhase> pins,
        EventVectorCoverageWitness fresh, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        if (entries.Length != pins.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        admission.RequireEntryCount(entries.Length);
        var bytes = checked((long)entries.Length * sizeof(long));
        admission.RequireEncodedBytes(bytes);
        work.ChargeBytes(bytes);
        var epochs = ImmutableArray.CreateBuilder<long>(entries.Length);
        for (var ordinal = FirstOrdinal; ordinal < entries.Length; ordinal++)
        {
            work.Check();
            var epoch = Find(entries[ordinal], pins[ordinal], fresh, admission, work);
            if (request.Action == EventFeedControlAction.Acknowledge
                && epoch != pins[ordinal].SourcePolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
            }
            epochs.Add(epoch);
        }
        return epochs.MoveToImmutable();
    }

    private static long Find(EventVectorEntry retained, EventVectorSourcePhase pin,
        EventVectorCoverageWitness fresh, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var epoch = EmptyPolicyEpoch;
        foreach (var packet in fresh.FreshPackets)
        {
            admission.RequireEncodedBytes(packet.OriginalCaptureBytes.Length);
            work.ChargeBytes(packet.OriginalCaptureBytes.Length);
            var capture = NativeSerialization.Deserialize<EventVectorCoverageCapture>(packet.OriginalCaptureBytes.Span);
            if (!PhysicalOwnerEntryValidation.SameOwner(capture.SourceOwner, pin.SourceOwner))
            {
                continue;
            }
            foreach (var current in EventVectorEntryEncoding.Decode(capture.OriginalEncodedEntries, capture.EntryChecksum, admission))
            {
                if (current.Source != retained.Source)
                {
                    continue;
                }
                Require(retained, current, capture);
                if (epoch != EmptyPolicyEpoch)
                {
                    throw Errors.Fail(ErrorCode.Corruption, Invalid);
                }
                epoch = capture.PolicyEpoch;
            }
        }
        if (epoch <= EmptyPolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
        }
        return epoch;
    }

    private static void Require(EventVectorEntry retained, EventVectorEntry current, EventVectorCoverageCapture capture)
    {
        if (current.PrincipalId != retained.PrincipalId || capture.PrincipalId != retained.PrincipalId
            || current.PolicyEpoch != capture.PolicyEpoch || current.Incarnation != retained.Incarnation
            || current.PlacementEpoch != retained.PlacementEpoch || current.SchemaVersion != retained.SchemaVersion
            || current.LogicalPlacementRevision != retained.LogicalPlacementRevision
            || current.DirectoryFence != retained.DirectoryFence)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
        }
    }
}
