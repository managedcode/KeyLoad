using KeyLoad.Core;
using KeyLoad.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class AnnProjectionObservationRead
{
    internal static async Task<AnnProjectionObservationResult> ExecuteAsync(DatabaseEngine database,
        IServiceProvider services, TimeProvider clock, PrincipalRecord principal,
        DecodedGrainRequest original, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!services.GetRequiredService<IOptions<QueryExecutionOptions>>().Value.EnableApproximateSearch)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, AnnSearchProtocol.Disabled); }
        var request = GrainNativePayload.Read<AnnProjectionObservationRequest>(original.Payload);
        RequireRequest(request);
        var work = new ReadExecutionBudget(services.GetRequiredService<IOptions<DatabaseLimits>>(), clock, cancellationToken);
        work.ConstrainLifetime(original.Envelope.ExpiresAt);
        work.ConstrainNativeWork(request.MaximumReadBytes, request.MaximumRecords);
        work.ChargeBytes(original.Payload.Length);
        var admitted = RequireScope(database, services, principal, request, work);
        WaitForAnnIndexResult? waited = null;
        AnnSearchPage? approximate = null;
        var search = services.GetRequiredService<SearchEngine>();
        if (request.Kind == AnnProjectionObservationKind.Wait)
        {
            waited = await search.WaitForAnnIndexAsync(principal.Id, request.Wait!, work, cancellationToken)
                .ConfigureAwait(true);
        }
        else if (request.Kind == AnnProjectionObservationKind.Approximate)
        {
            approximate = await search.ApproximateSearchAsync(principal.Id, request.Approximate!, work, cancellationToken)
                .ConfigureAwait(true);
        }
        var current = GrainRequestAuthority.ReloadForRequest(database, original.Envelope, clock);
        if (current.Id != principal.Id || current.PolicyEpoch != principal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid); }
        if (RequireScope(database, services, current, request, work) != admitted)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid); }
        var actual = database.Store.Identity;
        work.Check();
        return new(AnnProjectionObservationProtocol.Version, actual.NodeId, actual.ReadGeneration, waited,
            work.ReadBytes, work.ConstrainedNativeRecords, approximate);
    }

    private static void RequireRequest(AnnProjectionObservationRequest request)
    {
        if (request is null || request.Version != AnnProjectionObservationProtocol.Version
            || !Enum.IsDefined(request.Kind) || request.OriginalRequest is null
            || request.OriginalRequest.CommandId == Guid.Empty || request.OriginalRequest.NodeId == Guid.Empty
            || request.OriginalRequest.Consumer is null || request.OriginalRequest.Consumer.Partition is null
            || request.OriginalRequest.Placement is null || request.OriginalRequest.Space is null
            || string.IsNullOrEmpty(request.OriginalRequest.Collection) || string.IsNullOrEmpty(request.OriginalRequest.Field)
            || request.Approximate is { Search: null }
            || request.OriginalRequest.IndexGeneration < AnnProjectionObservationProtocol.FirstGeneration
            || request.MaximumReadBytes <= AnnProjectionObservationProtocol.NoReadWork
            || request.MaximumRecords <= AnnProjectionObservationProtocol.NoReadWork
            || request.Kind == AnnProjectionObservationKind.Identity && (request.Wait is not null || request.Approximate is not null)
            || request.Kind == AnnProjectionObservationKind.Wait && (request.Wait is null || request.Approximate is not null)
            || request.Kind == AnnProjectionObservationKind.Approximate && (request.Approximate is null || request.Wait is not null))
        { throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid); }
        var expected = request.OriginalRequest;
        if (request.Wait is { } wait && (wait.Consumer != expected.Consumer || wait.Partition != expected.Consumer.Partition
                || wait.Collection != expected.Collection || wait.VectorField != expected.Field
                || wait.Space != expected.Space || wait.IndexGeneration != expected.IndexGeneration)
            || request.Approximate is { } approximate && (approximate.Consumer != expected.Consumer
                || approximate.Search.Partition != expected.Consumer.Partition || approximate.Search.Collection != expected.Collection
                || approximate.Search.VectorField != expected.Field || approximate.Search.Space != expected.Space
                || approximate.IndexGeneration != expected.IndexGeneration))
        { throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid); }
    }

    private static LocalAnnObservationFence RequireScope(DatabaseEngine database, IServiceProvider services,
        PrincipalRecord principal, AnnProjectionObservationRequest request, ReadExecutionBudget work)
    {
        var original = request.OriginalRequest;
        var expected = services.GetRequiredService<PhysicalShardRecord>();
        if (!AnnProjectionObservationScope.SamePhysicalIdentity(expected, original.Placement)
            || request.Kind != AnnProjectionObservationKind.Identity && original.NodeId != database.Store.Identity.NodeId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionObservationProtocol.Invalid); }
        return database.CaptureLocalAnnObservation(principal.Id, original, work);
    }
}
