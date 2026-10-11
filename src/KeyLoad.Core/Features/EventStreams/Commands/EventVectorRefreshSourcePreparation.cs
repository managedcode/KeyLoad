using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

internal static class EventVectorRefreshSourcePreparation
{
    private const long CoverageStep = 1;
    private const long EmptyPolicyEpoch = 0;
    private const string Invalid = "The original event vector refresh source requires recovery.";

    internal static EventVectorSourcePhase Candidate(EventVectorPreparedRefresh prepared, int ordinal,
        DateTimeOffset expiry, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var target = prepared.Candidates[ordinal];
        var prior = prepared.Original.Entries.FirstOrDefault(entry => entry.Source == target.Source);
        if (prior is null)
        {
            var witness = EventVectorCoverageWitnessEncoding.Read(prepared.Original.OriginalCoverageWitness,
                prepared.Request, admission);
            using var decoded = new EventVectorFirstOpenDecodedOwner(witness);
            var sourceOwner = EventVectorOriginalEntryOwner.Require(witness, target, admission, work);
            var next = prepared.Map with { CoverageGeneration = checked(prepared.Map.CoverageGeneration + CoverageStep) };
            var mutation = EventVectorOpenPinMutation.Create(next, target, sourceOwner, prepared.ControlOwner, expiry);
            return Encode(prepared.Map, ordinal, target, mutation, admission, work);
        }
        var index = prepared.Original.Entries.IndexOf(prior);
        var settled = ReadPin(prepared, index, admission, work);
        EventVectorSourceMutation? advance = null;
        try
        {
            advance = EventVectorSettledSourceMutation.Create(prepared.Map, prior, target, settled,
                EventVectorSourcePhaseRole.Advance, expiry, target.PolicyEpoch, admission) with
            { CoverageGeneration = checked(prepared.Map.CoverageGeneration + CoverageStep) };
            return Encode(prepared.Map, ordinal, prior, advance, admission, work);
        }
        finally { Clear(settled, advance); }
    }

    internal static EventVectorSourcePhase Removed(EventVectorPreparedRefresh prepared, int ordinal,
        DateTimeOffset expiry, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var prior = prepared.Original.Entries[ordinal];
        if (prepared.Candidates.Any(entry => entry.Source == prior.Source))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var settled = ReadPin(prepared, ordinal, admission, work);
        EventVectorSourceMutation? release = null;
        try
        {
            var epoch = CurrentPolicy(prepared, settled, admission, work);
            release = EventVectorSettledSourceMutation.Create(prepared.Map, prior, prior, settled,
                EventVectorSourcePhaseRole.Release, expiry, epoch, admission);
            return Encode(prepared.Map, ordinal, prior, release, admission, work);
        }
        finally { Clear(settled, release); }
    }

    private static EventVectorSourcePhase ReadPin(EventVectorPreparedRefresh prepared, int ordinal,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var bytes = prepared.Original.OriginalPinPhaseBytes[ordinal];
        admission.RequireEncodedBytes(bytes.Length);
        work.ChargeBytes(bytes.Length);
        return NativeSerialization.Deserialize<EventVectorSourcePhase>(bytes.Span);
    }

    private static EventVectorSourcePhase Encode(EventVectorMap map, int ordinal, EventVectorEntry original,
        EventVectorSourceMutation mutation, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        work.Check();
        work.ChargeBytes(NativeSerialization.Measure(mutation));
        work.ChargeBytes(System.Security.Cryptography.SHA256.HashSizeInBytes);
        return EventVectorPhasePreparation.Prepare(map with { State = EventVectorMapState.Refreshing },
            ordinal, original, mutation, admission);
    }

    private static long CurrentPolicy(EventVectorPreparedRefresh prepared, EventVectorSourcePhase settled,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var witness = EventVectorCoverageWitnessEncoding.Read(prepared.Original.OriginalCoverageWitness,
            prepared.Request, admission);
        using var decoded = new EventVectorFirstOpenDecodedOwner(witness);
        long? epoch = null;
        foreach (var packet in witness.OriginalPackets)
        {
            admission.RequireEncodedBytes(packet.OriginalCaptureBytes.Length);
            work.ChargeBytes(packet.OriginalCaptureBytes.Length);
            var capture = NativeSerialization.Deserialize<EventVectorCoverageCapture>(packet.OriginalCaptureBytes.Span);
            try
            {
                if (!PhysicalOwnerEntryValidation.SameOwner(capture.SourceOwner, settled.SourceOwner))
                { continue; }
                if (epoch is not null || capture.PrincipalId != prepared.Map.PrincipalId || capture.PolicyEpoch <= EmptyPolicyEpoch)
                { throw Errors.Fail(ErrorCode.PermissionDenied, Invalid); }
                epoch = capture.PolicyEpoch;
            }
            finally { EventVectorCoverageCaptureBuffers.Clear(capture); }
        }
        return epoch ?? throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
    }

    private static void Clear(EventVectorSourcePhase settled, EventVectorSourceMutation? mutation)
    {
        EventVectorSettlementTargetEncoding.Clear(settled.OriginalNativeBody);
        EventVectorSettlementTargetEncoding.Clear(settled.OriginalBodyDigest);
        EventVectorSettlementTargetEncoding.Clear(settled.SourceObservationWitness);
        EventVectorSettlementTargetEncoding.Clear(settled.SourceOriginalOutcomeDigest);
        if (mutation is not null)
        { EventVectorSettlementTargetEncoding.Clear(mutation.OriginalPinNativeBody); }
    }
}
