using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query;

namespace KeyLoad.Server.Features.Search;

internal sealed record RemoteAnnReadSelection(ProjectionConsumerRef Consumer, string Collection,
    string Field, VectorSpace Space, long Generation, WaitForAnnIndexRequest? Wait,
    ApproximateSearchRequest? Approximate)
{
    internal static RemoteAnnReadSelection Decode(GrainReadKind kind, ReadOnlyMemory<byte> payload)
    {
        if (kind == GrainReadKind.WaitForAnnIndex)
        {
            var request = GrainNativePayload.Read<WaitForAnnIndexRequest>(payload);
            if (request.Consumer is null || request.Partition != request.Consumer.Partition)
            {
                throw Errors.Fail(ErrorCode.Validation, AnnProjectionObservationProtocol.Invalid);
            }
            return new(request.Consumer, request.Collection, request.VectorField, request.Space,
                request.IndexGeneration, request, null);
        }
        if (kind == GrainReadKind.ApproximateSearch)
        {
            var request = GrainNativePayload.Read<ApproximateSearchRequest>(payload);
            if (request.Consumer is null || request.Search is null || request.Consumer.Partition != request.Search.Partition
                || request.Search.VectorField is null || request.Search.Space is null)
            {
                throw Errors.Fail(ErrorCode.Validation, AnnProjectionObservationProtocol.Invalid);
            }
            return new(request.Consumer, request.Search.Collection, request.Search.VectorField, request.Search.Space,
                request.IndexGeneration, null, request);
        }
        throw Errors.Fail(ErrorCode.UnsupportedCapability, AnnProjectionObservationProtocol.Invalid);
    }

    internal async Task<object> ExecuteLocalAsync(SearchEngine actual, string principal,
        ReadExecutionBudget budget, AnnReadLifetimeMetadata metadata, CancellationToken cancellationToken)
    {
        if (Wait is not null)
        {
            return await actual.WaitForAnnIndexAsync(principal, Wait, budget, metadata, cancellationToken).ConfigureAwait(false);
        }
        return await actual.ApproximateSearchAsync(principal, Approximate!, budget, metadata, cancellationToken).ConfigureAwait(false);
    }

    internal AnnProjectionObservationRequest ForOriginal(AnnMaintenanceRequest original, ReadExecutionBudget budget)
        => new(AnnProjectionObservationProtocol.Version,
            Wait is not null ? AnnProjectionObservationKind.Wait : AnnProjectionObservationKind.Approximate,
            original, budget.RemainingReadGrantBytes, budget.RemainingReadGrantRecords, Wait, Approximate);

    internal object ReadResult(AnnProjectionObservationResult result)
    {
        if (Wait is not null && result.Wait is not null && result.Approximate is null)
        {
            return result.Wait;
        }
        if (Approximate is not null && result.Approximate is not null && result.Wait is null)
        {
            return result.Approximate;
        }
        throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
    }
}
