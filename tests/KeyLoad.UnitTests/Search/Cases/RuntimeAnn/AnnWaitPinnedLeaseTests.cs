using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnWaitPinnedLeaseTests
{
    [Test]
    public Task ActualPinnedResidentFrameRejectsWaitThenJoinedReaderReleaseAllowsSameLiteralIndexedPrefix()
        => AnnWaitWholeFlow.RunAsync(async (fixture, runtime, pin, receipt) =>
        {
            var database = fixture.Canonical;
            var request = AnnWaitWholeFlow.Request(database, pin, receipt);
            var image = AnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            IAnnProjectionLease? held = null;
            var failures = new List<Exception>();
            try
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var budget = new ReadExecutionBudget(database.Database.OperationLimitsOptions, database.Database.EvaluationClock);
                    held = database.Database.WithQueryView(AnnWaitWholeFlow.Root, request.Partition,
                        request.Collection, (view, principal, resource) =>
                        {
                            database.Database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
                            database.Database.Authorization.RequireFieldUse(principal, resource, request.VectorField);
                            return runtime.Owner.Acquire(view, new(request.Partition, request.Collection, request.VectorField,
                                request.Space, request.Consumer, request.IndexGeneration), budget);
                        });
                    await Assert.That(held.IndexedAppliedPosition).IsEqualTo(receipt.Token.Position);
                    WaitForAnnIndexResult? partial = null;
                    var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                        await AnnPublicWholeFlow.Engine(database, runtime).WaitForAnnIndexAsync(AnnWaitWholeFlow.Root,
                            request, TestContext.Current!.Execution.CancellationToken)) ?? throw new InvalidOperationException();
                    await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
                    await Assert.That(partial).IsNull();
                    await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
                }, failures);
            }
            finally { if (held is not null) { ServerFailureObserver.Observe(held.Dispose, failures); } }
            ServerFailureObserver.ThrowIfAny(failures);
            await AnnWaitWholeFlow.HealthyAsync(database, runtime, request, TestContext.Current!.Execution.CancellationToken);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });
}
