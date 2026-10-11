using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Orleans;

internal static class EventVectorReadCapabilities
{
    private const int Version = 1;
    private const string Invalid = "The private event vector receiving read is inconsistent.";

    internal static object Execute(DatabaseEngine database, IServiceProvider services, PrincipalRecord principal,
        DecodedGrainRequest request, CancellationToken originalCancellationToken)
    {
        originalCancellationToken.ThrowIfCancellationRequested();
        if (!EventVectorRequestScope.HandlesRead(request.Envelope.ReadKind!.Value)
            || request.Envelope.Purpose != EventVectorRequestScope.SourcePurpose)
        { throw Errors.Fail(ErrorCode.PermissionDenied, Invalid); }
        var owner = services.GetRequiredService<PhysicalShardRecord>();
        var work = database.CreateEventVectorReadWork(request.Envelope.ExpiresAt, originalCancellationToken);
        return request.Envelope.ReadKind.Value switch
        {
            GrainReadKind.EventVectorCoverage => Coverage(database, principal, request, owner, work,
                originalCancellationToken),
            GrainReadKind.EventVectorOriginalOutcome => Outcome(database, principal, request, owner, work, originalCancellationToken),
            GrainReadKind.EventVectorSources => Sources(database, principal, request, owner, work),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, Invalid)
        };
    }

    private static EventVectorCoverageCapture Coverage(DatabaseEngine database, PrincipalRecord principal,
        DecodedGrainRequest request, PhysicalShardRecord actualOwner, ReadExecutionBudget work,
        CancellationToken originalCancellationToken)
    {
        var input = GrainNativePayload.Read<EventVectorCoverageReadRequest>(request.Payload);
        RequireOwner(input.Version, input.SourceOwner, actualOwner);
        return database.CaptureEventVectorCoverage(principal.Id, input.OriginalRequest, input.GroupOrdinal,
            input.SourceOwner, input.ControlOwner, work, originalCancellationToken);
    }

    private static object Outcome(DatabaseEngine database, PrincipalRecord principal,
        DecodedGrainRequest request, PhysicalShardRecord actualOwner, ReadExecutionBudget work,
        CancellationToken originalCancellationToken)
    {
        var input = GrainNativePayload.Read<EventVectorOutcomeReadRequest>(request.Payload);
        if (input.OriginalFirstIssuanceRequest is { } first)
        {
            if (input.Version != Version || first.Version != Version
                || input.OriginalSourcePhase is not null || input.OriginalControlRequest is not null
                || input.ControlOwner is null || first.OriginalPublicRequest is null
                || !input.FreshSourceAuthorizationWitness.IsEmpty || input.TailObservation)
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
            RequireOwner(input.Version, input.ControlOwner, actualOwner);
            RequireOwner(first.Version, first.ControlOwner, actualOwner);
            return database.ReadEventVectorFirstIssuance(principal.Id, first.OriginalPublicRequest,
                actualOwner, work, originalCancellationToken);
        }
        if (input.OriginalSourcePhase is { } source)
        {
            if (input.OriginalControlRequest is not null || input.ControlOwner is not null
                || input.OriginalFirstIssuanceRequest is not null || !input.FreshSourceAuthorizationWitness.IsEmpty || input.TailObservation)
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
            RequireOwner(input.Version, source.SourceOwner, actualOwner);
            return database.ReadEventVectorSourceOutcome(principal.Id, source, work);
        }
        if (input.OriginalControlRequest is not { } control || input.ControlOwner is null)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        RequireOwner(input.Version, input.ControlOwner, actualOwner);
        if (input.TailObservation)
        {
            return database.ReadEventVectorTailOffer(principal.Id, control, actualOwner,
                input.FreshSourceAuthorizationWitness, work, originalCancellationToken);
        }
        return database.ReadEventVectorParentOutcome(principal.Id, control, actualOwner, input.FreshSourceAuthorizationWitness, work,
            originalCancellationToken);
    }

    private static System.Collections.Immutable.ImmutableArray<EventSourcePage> Sources(DatabaseEngine database, PrincipalRecord principal,
        DecodedGrainRequest request, PhysicalShardRecord actualOwner, ReadExecutionBudget work)
    {
        var input = GrainNativePayload.Read<EventVectorSourcesReadRequest>(request.Payload);
        RequireOwner(input.Version, input.SourceOwner, actualOwner);
        return database.ReadEventVectorPinnedSources(principal.Id, input, work);
    }

    private static void RequireOwner(int version, PhysicalShardRecord supplied, PhysicalShardRecord actual)
    {
        if (version != Version || supplied is null || !PhysicalOwnerEntryValidation.SameOwner(supplied, actual))
        { throw Errors.Fail(ErrorCode.OwnershipLost, Invalid); }
    }
}
