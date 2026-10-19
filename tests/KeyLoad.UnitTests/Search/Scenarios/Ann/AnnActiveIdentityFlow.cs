using KeyLoad.Clustering;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnActiveIdentityFlow
{
    private const int Count = 3;

    internal static async Task RunAsync(TestDatabase database)
    {
        var values = AnnActiveIdentityConfiguration.Create(database);
        var clone = await AnnActiveIdentityAssertions.CloneAsync(values.Request);
        var failures = new List<Exception>();
        AnnMaintenanceTestRuntime? runtime = null;
        AnnMaintenanceResult? completed = null;
        await ServerFailureObserver.ObserveAsync(() =>
        {
            runtime = new(database);
            return ExecuteAsync(database, runtime, values.Request, clone, values.Configured, value => completed = value);
        }, failures);
        if (runtime is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => runtime.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await AnnPublishedImageAssertions.AssertAsync(database, clone, completed!.IndexSha256!);
    }

    private static async Task ExecuteAsync(TestDatabase database, AnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest shared, AnnMaintenanceRequest clone, ProjectionConsumerInfo configured,
        Action<AnnMaintenanceResult> completed)
    {
        var principal = database.Store.Read(view => database.Database.Principal(view,
            AnnProjectionPinTestSupport.Principal, database.Database.EvaluationClock.GetUtcNow()));
        var token = TestContext.Current!.Execution.CancellationToken;
        await Assert.That(await runtime.Owner.AdmitOriginalAsync(principal, shared, configured, token)).IsNull();
        var began = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, shared, AnnMaintenanceCapabilityKind.Begin);
        await AnnActiveIdentityAssertions.RefuseAsync(database, runtime, clone);
        var result = await AnnActiveIdentityPhases.FinishAsync(database, runtime, clone, began.Source!.ThroughSequence);
        await Assert.That(result.Count).IsEqualTo(Count);
        await Assert.That(result.Checkpoint!.Checkpoint).IsEqualTo(began.Source.ThroughSequence);
        var before = AnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        await runtime.Owner.SettleOriginalResultAsync(principal, clone, result, token);
        await runtime.Owner.AbortAsync(runtime.SessionId);
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var replay = await runtime.Owner.AdmitOriginalAsync(principal, shared, configured, token);
        await Assert.That(OrleansSerialization.Serialize(replay!)).IsEquivalentTo(OrleansSerialization.Serialize(result), CollectionOrdering.Matching);
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        completed(result);
    }
}
