using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.Search;
using KeyLoad.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static partial class AnnProjectionObservationRead
{
    internal static async Task<AnnProjectionObservationResult> ExecuteAsync(DatabaseEngine database,
        IServiceProvider services, TimeProvider clock, PrincipalRecord principal,
        DecodedGrainRequest original, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        AnnProjectionObservationScope.Validate(original.Envelope);
        if (!services.GetRequiredService<IOptions<QueryExecutionOptions>>().Value.EnableApproximateSearch)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, AnnSearchProtocol.Disabled);
        }
        var request = GrainPayload.Read<AnnProjectionObservationRequest>(original.Payload);
        var metadata = Require(request, principal, database, clock);
        var work = new ReadExecutionBudget(clock, cancellationToken);
        work.AttachResourceLifetimeBinding(binding);
        var admitted = AnnProjectionObservationRead.RequireScope(database, services, principal, request, work, metadata, binding);
        var search = services.GetRequiredService<SearchEngine>();
        WaitForAnnIndexResult? waited = null;
        AnnSearchPage? approximate = null;
        if (request.Kind == AnnProjectionObservationKind.Wait)
        {
            waited = await search.WaitForAnnIndexBoundAsync(principal.Id, request.Wait!, work, metadata, binding, cancellationToken)
                .ConfigureAwait(true);
        }
        else if (request.Kind == AnnProjectionObservationKind.Approximate)
        {
            approximate = await search.ApproximateSearchBoundAsync(principal.Id, request.Approximate!, work, metadata, binding, cancellationToken)
                .ConfigureAwait(true);
        }
        var current = GrainRequestAuthority.ReloadForRequest(database, original.Envelope, clock);
        if (current.Id != principal.Id || current.PolicyEpoch != principal.PolicyEpoch
            || AnnProjectionObservationRead.RequireScope(database, services, current, request, work, metadata, binding) != admitted)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid);
        }
        var actual = database.Store.Identity;
        work.Check();
        var result = new AnnProjectionObservationResult(AnnProjectionObservationProtocol.Version, actual.NodeId, actual.ReadGeneration, waited,
            approximate);
        return result;
    }

    private static AnnReadLifetimeMetadata Require(AnnProjectionObservationRequest request,
        PrincipalRecord target, DatabaseEngine database, TimeProvider clock)
    {
        if (request is null || request.Definition is null || request.Coverage is null
            || request.Principal is null || request.Principal.Id != target.Id
            || request.Principal.Revoked
            || request.Principal.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
        }
        AnnProjectionObservationRead.RequireRequest(request);
        var source = request.OriginalRequest;
        if (request.Definition.Name != source.Collection || request.Definition.Kind != ResourceKind.Collection)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
        }
        var metadata = new AnnReadLifetimeMetadata(request.Definition, request.Coverage,
            request.Principal, request.Definitions);
        metadata.Require(source.Consumer.Partition);
        database.Authorization.Require(request.Principal, source.Consumer.Partition, source.Collection, Capability.VectorSearch);
        return metadata;
    }
}
