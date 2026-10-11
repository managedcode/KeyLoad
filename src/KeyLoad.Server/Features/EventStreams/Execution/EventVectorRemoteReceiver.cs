using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorRemoteReceiver(OrleansNode node, PartitionHost partition,
    EventVectorRemoteCallValidation authority, EventVectorNativeReceivingCall native,
    EventVectorRemoteEncoding encoding)
{
    internal ReplicaSiloDiscovery Discovery() => node.Discovery?.Read()
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable);

    internal ReadExecutionBudget CreateWork(EventVectorAdmittedCall admitted,
        CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        return partition.Database.CreateEventVectorReadWork(admitted.Original.Call.ExpiresAt, originalCancellationToken);
    }

    internal Task<ReadOnlyMemory<byte>> ExecuteAsync(EventVectorAdmittedCall admitted,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        var call = admitted.Original.Call;
        authority.Require(call, originalCancellationToken);
        return call.Action switch
        {
            EventVectorRemoteAction.Coverage => CoverageAsync(call, work, originalCancellationToken),
            EventVectorRemoteAction.SourceEffect => SourceAsync(call, submit: true, work, originalCancellationToken),
            EventVectorRemoteAction.OriginalOutcome => SourceAsync(call, submit: false, work, originalCancellationToken),
            EventVectorRemoteAction.SourceRead => SourceReadAsync(call, work, originalCancellationToken),
            EventVectorRemoteAction.ParentOutcome => ParentOutcomeAsync(call, work, originalCancellationToken),
            EventVectorRemoteAction.FirstIssuanceOutcome => FirstIssuanceAsync(call, work, originalCancellationToken),
            _ => throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid)
        };
    }

    private async Task<ReadOnlyMemory<byte>> CoverageAsync(EventVectorRemoteCall call,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        var input = NativeSerialization.Deserialize<EventVectorCoverageCall>(call.OriginalNativeRequest.Span);
        if (input is null || input.Version != EventVectorRemoteProtocol.Version
            || input.OriginalRequest is null || input.GroupOrdinal != authority.GroupOrdinal
            || string.IsNullOrWhiteSpace(input.PrincipalId))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        JsonData.Identifier(input.PrincipalId);
        var principal = native.Principal(input.PrincipalId, originalCancellationToken);
        var request = new EventVectorCoverageReadRequest(EventVectorRemoteProtocol.Version,
            input.OriginalRequest, input.GroupOrdinal, authority.LocalOwner.Owner, authority.ControlOwner.Owner);
        var capture = await EventVectorReceivingRead.ExecuteAsync<EventVectorCoverageCapture, EventVectorCoverageReadRequest>(native, call, principal,
            GrainReadKind.EventVectorCoverage, request, encoding, work, originalCancellationToken).ConfigureAwait(false);
        var actual = partition.Database.Store.Identity;
        if (capture.NodeId != actual.NodeId || capture.ReadGeneration != actual.ReadGeneration
            || capture.PrincipalId != principal.Id || capture.GroupOrdinal != input.GroupOrdinal
            || !PhysicalOwnerEntryValidation.SameOwner(capture.SourceOwner, authority.LocalOwner.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        try
        { return EventVectorWorkEncoding.Encode(capture, encoding, work); }
        finally { EventVectorCaptureBuffers.Clear(capture); }
    }

    private async Task<ReadOnlyMemory<byte>> SourceReadAsync(EventVectorRemoteCall call,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        var input = NativeSerialization.Deserialize<EventVectorSourceReadCall>(call.OriginalNativeRequest.Span);
        if (input is null || input.Version != EventVectorRemoteProtocol.Version
            || input.OriginalReadRequest is null || input.OriginalReadRequest.SourceOwner is null
            || input.OriginalReadRequest.Version != EventVectorRemoteProtocol.Version
            || string.IsNullOrWhiteSpace(input.PrincipalId)
            || !PhysicalOwnerEntryValidation.SameOwner(input.OriginalReadRequest.SourceOwner, authority.LocalOwner.Owner))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        JsonData.Identifier(input.PrincipalId);
        var principal = native.Principal(input.PrincipalId, originalCancellationToken);
        var owned = input.OriginalReadRequest with { SourceOwner = authority.LocalOwner.Owner };
        var pages = await EventVectorReceivingRead.ExecuteAsync<ImmutableArray<EventSourcePage>, EventVectorSourcesReadRequest>(native, call, principal,
            GrainReadKind.EventVectorSources, owned, encoding, work, originalCancellationToken).ConfigureAwait(false);
        return EventVectorWorkEncoding.Encode(pages, encoding, work);
    }

    private async Task<ReadOnlyMemory<byte>> ParentOutcomeAsync(EventVectorRemoteCall call,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        if (!PhysicalOwnerEntryValidation.SameOwner(authority.LocalOwner.Owner, authority.ControlOwner.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        var input = NativeSerialization.Deserialize<EventVectorParentLookupCall>(call.OriginalNativeRequest.Span);
        if (input is null || input.Version != EventVectorRemoteProtocol.Version
            || input.OriginalControlRequest is null || string.IsNullOrWhiteSpace(input.PrincipalId))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        JsonData.Identifier(input.PrincipalId);
        var principal = native.Principal(input.PrincipalId, originalCancellationToken);
        var request = new EventVectorOutcomeReadRequest(EventVectorRemoteProtocol.Version, null)
        {
            OriginalControlRequest = input.OriginalControlRequest,
            ControlOwner = authority.ControlOwner.Owner,
            FreshSourceAuthorizationWitness = input.FreshSourceAuthorizationWitness,
            TailObservation = input.TailObservation
        };
        var observation = await EventVectorReceivingRead.ExecuteAsync<EventVectorParentObservation, EventVectorOutcomeReadRequest>(native, call, principal,
            GrainReadKind.EventVectorOriginalOutcome, request, encoding, work, originalCancellationToken).ConfigureAwait(false);
        var actual = partition.Database.Store.Identity;
        if (observation.NodeId != actual.NodeId || observation.ReadGeneration != actual.ReadGeneration
            || observation.PrincipalId != principal.Id
            || !input.TailObservation && observation.OriginalPublicCommandId != input.OriginalControlRequest.CommandId
            || !PhysicalOwnerEntryValidation.SameOwner(observation.ControlOwner, authority.ControlOwner.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        return EventVectorWorkEncoding.Encode(observation, encoding, work);
    }

    private async Task<ReadOnlyMemory<byte>> FirstIssuanceAsync(EventVectorRemoteCall call,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        if (!PhysicalOwnerEntryValidation.SameOwner(authority.LocalOwner.Owner, authority.ControlOwner.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        var input = NativeSerialization.Deserialize<EventVectorFirstLookupCall>(call.OriginalNativeRequest.Span);
        if (input is null || input.Version != EventVectorRemoteProtocol.Version
            || input.OriginalFirstIssuanceRequest is null
            || input.OriginalFirstIssuanceRequest.Version != EventVectorRemoteProtocol.Version
            || input.OriginalFirstIssuanceRequest.OriginalPublicRequest is null
            || !PhysicalOwnerEntryValidation.SameOwner(input.OriginalFirstIssuanceRequest.ControlOwner,
                authority.ControlOwner.Owner) || string.IsNullOrWhiteSpace(input.PrincipalId))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        JsonData.Identifier(input.PrincipalId);
        var principal = native.Principal(input.PrincipalId, originalCancellationToken);
        var request = new EventVectorOutcomeReadRequest(EventVectorRemoteProtocol.Version, null)
        {
            ControlOwner = authority.ControlOwner.Owner,
            OriginalFirstIssuanceRequest = new(EventVectorRemoteProtocol.Version,
                input.OriginalFirstIssuanceRequest.OriginalPublicRequest, authority.ControlOwner.Owner)
        };
        var observation = await EventVectorReceivingRead.ExecuteAsync<EventVectorFirstIssuanceObservation, EventVectorOutcomeReadRequest>(native, call, principal,
            GrainReadKind.EventVectorOriginalOutcome, request, encoding, work, originalCancellationToken).ConfigureAwait(false);
        var actual = partition.Database.Store.Identity;
        if (observation.NodeId != actual.NodeId || observation.ReadGeneration != actual.ReadGeneration
            || observation.PrincipalId != principal.Id
            || observation.OriginalPublicCommandId != input.OriginalFirstIssuanceRequest.OriginalPublicRequest.CommandId
            || !PhysicalOwnerEntryValidation.SameOwner(observation.ControlOwner, authority.ControlOwner.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        return EventVectorWorkEncoding.Encode(observation, encoding, work);
    }

    private async Task<ReadOnlyMemory<byte>> SourceAsync(EventVectorRemoteCall call, bool submit,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        var phase = NativeSerialization.Deserialize<EventVectorSourcePhase>(call.OriginalNativeRequest.Span);
        RequireSource(phase);
        var principal = native.Principal(phase.PrincipalId, originalCancellationToken);
        if (submit)
        { await native.SubmitAsync(call, principal, phase, work, originalCancellationToken).ConfigureAwait(false); }
        var request = new EventVectorOutcomeReadRequest(EventVectorRemoteProtocol.Version, phase);
        var observation = await EventVectorReceivingRead.ExecuteAsync<EventVectorSourceObservation, EventVectorOutcomeReadRequest>(native, call, principal,
            GrainReadKind.EventVectorOriginalOutcome, request, encoding, work, originalCancellationToken).ConfigureAwait(false);
        var actual = partition.Database.Store.Identity;
        if (observation.NodeId != actual.NodeId || observation.ReadGeneration != actual.ReadGeneration
            || observation.PrincipalId != principal.Id || observation.Source != phase.Source
            || observation.OriginalSourcePhaseCommandId != phase.PhaseCommandId
            || !observation.OriginalBodyDigest.Span.SequenceEqual(phase.OriginalBodyDigest.Span)
            || !PhysicalOwnerEntryValidation.SameOwner(observation.SourceOwner, authority.LocalOwner.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        return EventVectorWorkEncoding.Encode(observation, encoding, work);
    }

    private void RequireSource(EventVectorSourcePhase phase)
    {
        if (phase is null || phase.Version != EventVectorRemoteProtocol.Version
            || phase.SourceOwner is null || phase.ControlOwner is null || phase.Source is null
            || !PhysicalOwnerEntryValidation.SameOwner(phase.SourceOwner, authority.LocalOwner.Owner)
            || !PhysicalOwnerEntryValidation.SameOwner(phase.ControlOwner, authority.ControlOwner.Owner)
            || phase.ControlIncarnation != authority.ControlOwner.Owner.Incarnation
            || string.IsNullOrWhiteSpace(phase.PrincipalId))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        JsonData.Identifier(phase.PrincipalId);
    }
}
