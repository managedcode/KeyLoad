using KeyLoad.Core;
using KeyLoad.Core.Search;

namespace KeyLoad.Server.Search;

internal static class AnnReleaseValidation
{
    internal static void Require(DatabaseEngine database, PrincipalRecord principal,
        AnnMaintenanceRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        database.Store.Read(view =>
        {
            var current = database.Principal(view, principal.Id, database.EvaluationClock.GetUtcNow());
            _ = ProjectionConsumerAuthority.RequireReleasedProjectionConsumer(view, current, request.Consumer, request.IndexGeneration);
            AnnProjectionPinValidation.RequireOwner(database, view, current, request.Consumer.Partition, request);
            token.ThrowIfCancellationRequested();
            return true;
        });
    }
}
