using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorControlOpen
{
    private const int Version = 1;
    private const int PageOrdinal = 0;
    private const int PageCount = 1;
    private const long FirstRevision = 1;
    private const string InvalidOpen = "The original complete event vector open is inconsistent.";

    internal static EventVectorMap Execute(IAtomicTransaction transaction, PrincipalRecord principal,
        EventVectorControlPhase phase, Guid incarnation, DateTimeOffset expiresAt,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var request = phase.OriginalRequest;
        if (phase.Action != EventVectorControlAction.Open || expiresAt <= default(DateTimeOffset)
            || transaction.GetRecord<EventVectorMap>(EventVectorKeys.Map(request.ControlPartition, request.MapId)) is not null)
        { throw Errors.Fail(ErrorCode.Conflict, InvalidOpen); }
        var witness = EventVectorCoverageWitnessEncoding.Read(phase.OriginalCoverageWitness, request, admission);
        var projection = NativeSerialization.Deserialize<EventVectorCoverageSemanticIdentity>(
            witness.OriginalSemanticProjectionBytes.Span);
        if (!phase.OriginalEncodedEntries.Span.SequenceEqual(projection.OriginalEncodedEntries.Span))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOpen); }
        var page = new EventVectorPage
        {
            Version = Version,
            MapId = request.MapId,
            PageOrdinal = PageOrdinal,
            Entries = phase.OriginalEncodedEntries,
            Checksum = phase.OriginalEntryChecksum
        };
        var proof = EventVectorControlProof.Create(phase, FirstRevision, admission);
        var payload = ImmutableArray.Create(
            EventVectorEncodedRow.Create(EventVectorKeys.Page(request.ControlPartition, request.MapId, PageOrdinal), page, admission),
            EventVectorEncodedRow.Create(EventVectorKeys.Proof(request.ControlPartition, request.MapId, FirstRevision), proof, admission));
        var map = new EventVectorMap
        {
            Version = Version,
            MapId = request.MapId,
            ControlPartition = request.ControlPartition,
            PrincipalId = principal.Id,
            ControlPolicyEpoch = principal.PolicyEpoch,
            ControlIncarnation = incarnation,
            Revision = FirstRevision,
            CoverageGeneration = FirstRevision,
            SourceManifestDigest = witness.OriginalSemanticDigest,
            PageCount = PageCount,
            ExpiresAt = expiresAt,
            StartPolicy = (EventVectorStartPolicy)request.Start,
            OriginalOpenCommandId = request.CommandId,
            OriginalOptionsDigest = EventVectorControlBodyProjection.RequestDigest(request, admission),
            State = EventVectorMapState.Opening,
            OriginalSourceManifestDigest = witness.OriginalSemanticDigest
        };
        if (phase.IntendedSourcePhase is { } first)
        {
            EventVectorControlFirstPin.Require(map, phase, first, admission);
            payload = payload.Add(EventVectorEncodedRow.Create(
                EventVectorKeys.Phase(map.ControlPartition, map.MapId, first.PhaseCommandId), first, admission));
            map = map with { PendingPhaseId = first.PhaseCommandId };
        }
        return EventVectorMapWriter.Write(transaction, map, payload, options, cancellationToken);
    }
}
