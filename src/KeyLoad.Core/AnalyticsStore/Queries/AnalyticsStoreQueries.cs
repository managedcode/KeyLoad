using KeyLoad.Core.AnalyticsStore;

namespace KeyLoad.Core;
/// <summary>Executes AnalyticsStoreQueries operations against the caller's database context.</summary>
public static class AnalyticsStoreQueries
{
    internal const string AnalyticsCutChanged = "The analytics source cut or authorization changed.";
    /// <summary>Revalidates the original principal, lifetime, schema and source generation before derived output publication.</summary>
    /// <param name = "snapshot">The original snapshot whose budget remains owned by this operation.</param>

    /// <param name = "engine">The shared database execution context.</param>
    public static void ValidateAnalyticsAggregatePublication(this DatabaseEngine engine, AnalyticsAggregateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Work.Check();
        engine.Store.Read(raw =>
        {
            var view = snapshot.Work.CreateView(raw);
            var request = snapshot.Request;
            var scope = AnalyticsStoreReadScope.Open(engine, view, snapshot.Principal.Id, request.Partition, request.Store);
            if (scope.Principal.PolicyEpoch != snapshot.Principal.PolicyEpoch || JsonData.Fingerprint(scope.Lifetime) != JsonData.Fingerprint(snapshot.Lifetime) || scope.Resource.SchemaVersion != snapshot.Resource.SchemaVersion || engine.Store.Identity.Incarnation != snapshot.SourceIncarnation || engine.Store.Identity.ReadGeneration != snapshot.SourceReadGeneration || AnalyticsStoreReadScope.Generation(view, request.Partition, request.Store) != snapshot.SourceGeneration)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, AnalyticsCutChanged);
            }

            AnalyticsAggregateValidation.Require(engine, scope, request);
            snapshot.Work.Check();
            return true;
        });
    }
}
