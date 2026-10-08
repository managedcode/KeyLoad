using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnPublicPinnedLeaseTests
{
    private const int Threshold = 3;
    private const int Breadth = 3;

    [Test]
    public Task ActualResidentPinnedReadRejectsCompetingPublicFrameThenReleasedLeaseReturnsLiteralHealthyPage()
        => AnnPublicWholeFlow.RunAsync(Threshold, Breadth, async (database, runtime, pin) =>
        {
            var image = NativeAnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var request = AnnPublicWholeFlow.Request(database, pin);
            IAnnProjectionLease? held = null;
            var failures = new List<Exception>();
            try
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var budget = new ReadExecutionBudget(database.Database.OperationLimitsOptions, database.Database.EvaluationClock);
                    held = database.Database.WithQueryView(AnnProjectionPinTestSupport.Principal, request.Search.Partition,
                        request.Search.Collection, (view, principal, resource) =>
                        {
                            database.Database.Authorization.Require(principal, request.Search.Partition,
                                request.Search.Collection, Capability.VectorSearch);
                            database.Database.Authorization.RequireFieldUse(principal, resource, request.Search.VectorField!);
                            return runtime.Owner.Acquire(view, request, budget);
                        });
                    var engine = AnnPublicWholeFlow.Engine(database, runtime);
                    AnnSearchPage? partial = null;
                    var rejected = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                        await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request))
                        ?? throw new InvalidOperationException("The competing native resident frame was not rejected.");
                    await Assert.That(rejected.Code).IsEqualTo(ErrorCode.ResourceExhausted);
                    await Assert.That(partial).IsNull();
                    await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
                }, failures);
            }
            finally { if (held is not null) { ServerFailureObserver.Observe(held.Dispose, failures); } }
            ServerFailureObserver.ThrowIfAny(failures);
            await AnnPublicWholeFlow.PageAsync(await AnnPublicWholeFlow.Engine(database, runtime)
                .ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request), database, cut, AnnPageMode.Exact);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });
}
