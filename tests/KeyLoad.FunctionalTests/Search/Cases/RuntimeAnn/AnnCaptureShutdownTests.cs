using KeyLoad.Clustering;
using KeyLoad.Query.Search;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnCaptureShutdownTests
{
    private const int Count = 3;

    [Test]
    public Task ActualPinnedGenerationReaderJoinsBeforeServiceShutdownThenNewOwnerReadsTheLiteralPage()
        => AnnPublicWholeFlow.RunAsync(Count, Count, async (database, runtime, pin) =>
        {
            var image = AnnMaintenanceTestData.Snapshot(database);
            var position = database.Store.Position;
            var request = AnnPublicWholeFlow.Request(database, pin);
            var budget = new ReadExecutionBudget();
            IAnnProjectionLease? held = null;
            Task? shutdown = null;
            var failures = new List<Exception>();
            try
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    held = database.Database.WithQueryView(AnnProjectionPinTestSupport.Principal,
                        request.Search.Partition, request.Search.Collection, (view, principal, resource) =>
                        {
                            database.Database.Authorization.Require(principal, request.Search.Partition,
                                request.Search.Collection, Capability.VectorSearch);
                            database.Database.Authorization.RequireFieldUse(principal, resource, request.Search.VectorField!);
                            return runtime.Owner.Acquire(view, new(request.Search.Partition, request.Search.Collection,
                                request.Search.VectorField!, request.Search.Space!, request.Consumer, request.IndexGeneration), budget);
                        });
                    await Assert.That(held.Records.Length).IsEqualTo(Count);
                    shutdown = runtime.Owner.DisposeAsync().AsTask();
                    await Assert.That(shutdown.IsCompleted).IsFalse();
                    await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
                        AnnPublicWholeFlow.Engine(database, runtime).ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request));
                    await AnnPublicWholeFlow.UnchangedAsync(database, image, position);
                }, failures);
            }
            finally
            {
                if (held is not null)
                {
                    ServerFailureObserver.Observe(held.Dispose, failures);
                }
                if (shutdown is not null)
                {
                    await ServerFailureObserver.ObserveAsync(() => shutdown, failures);
                }
            }
            ServerFailureObserver.ThrowIfAny(failures);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, position);
            await HealthyAsync(database, pin, position);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, position);
        });

    private static async Task HealthyAsync(TestDatabase database, AnnMaintenanceRequest pin, long position)
    {
        await using var reopened = new AnnMaintenanceTestRuntime(database,
            configuration: AnnPublicWholeFlow.Configuration(Count, Count));
        var request = pin with { Mode = AnnMaintenanceMode.Restore };
        var began = await reopened.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Begin);
        if (began.OriginalCheckpointIntent is { } intent)
        {
            _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
        }
        _ = await reopened.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Load);
        _ = await reopened.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Verify);
        var result = await reopened.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Publish);
        await Assert.That(result.Count).IsEqualTo(Count);
        await AnnPublicWholeFlow.PageAsync(await AnnPublicWholeFlow.Engine(database, reopened)
            .ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, AnnPublicWholeFlow.Request(database, request)),
            database, position, AnnPageMode.Exact);
    }
}
