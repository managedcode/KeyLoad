using KeyLoad.Core.Features.AnalyticsStore;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string AnalyticsCutChanged = "The analytics source cut or authorization changed.";
    /// <summary>Revalidates the original principal, lifetime, schema and source generation before derived output publication.</summary>
    /// <param name="snapshot">The original snapshot whose budget remains owned by this operation.</param>
    public void ValidateAnalyticsAggregatePublication(AnalyticsAggregateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Work.Check();
        Store.Read(raw =>
        {
            var view = snapshot.Work.CreateView(raw);
            var request = snapshot.Request;
            var scope = AnalyticsStoreReadScope.Open(this, view, snapshot.Principal.Id, request.Partition, request.Store);
            if (scope.Principal.PolicyEpoch != snapshot.Principal.PolicyEpoch
                || JsonData.Fingerprint(scope.Lifetime) != JsonData.Fingerprint(snapshot.Lifetime)
                || scope.Resource.SchemaVersion != snapshot.Resource.SchemaVersion
                || Store.Identity.Incarnation != snapshot.SourceIncarnation
                || Store.Identity.ReadGeneration != snapshot.SourceReadGeneration
                || AnalyticsStoreReadScope.Generation(view, request.Partition, request.Store) != snapshot.SourceGeneration)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, AnalyticsCutChanged);
            }

            AnalyticsAggregateValidation.Require(this, scope, request);
            snapshot.Work.Check();
            return true;
        });
    }
}
