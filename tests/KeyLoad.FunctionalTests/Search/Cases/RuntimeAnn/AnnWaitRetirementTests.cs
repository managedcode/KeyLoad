using KeyLoad.Query.Search;
using KeyLoad.Server;
using KeyLoad.Server.Search;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnWaitRetirementTests
{
    private const long NextGeneration = 2;
    private const long StartAfter = 0;
    private const string NextConsumer = "ann-wait-next-pin";
    private static readonly string[] Kinds = ["putDocument", "patchDocument", "deleteDocument", "putVector", "applyVectorProjection"];

    [Test]
    public Task ReleasedGenerationKeepsActualPinnedIndexUntilReaderJoinsThenNewGenerationReturnsHealthyPrefix()
        => AnnWaitWholeFlow.RunAsync(async (fixture, runtime, pin, receipt) =>
        {
            var database = fixture.Canonical;
            var request = AnnWaitWholeFlow.Request(database, pin, receipt);
            IAnnProjectionLease? held = null;
            var failures = new List<Exception>();
            try
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    held = Acquire(database, runtime, request);
                    var original = AnnFixtureFileSnapshot.Capture(database.Directory)
                        .Where(row => Path.GetFileName(row.Path) == AnnProtocol.IndexFile).ToArray();
                    await Assert.That(original).IsNotEmpty();
                    _ = AnnProjectionPinTestSupport.Release(database, pin.Consumer, generation: pin.IndexGeneration).Get<ProjectionConsumerInfo>();
                    _ = await runtime.PhaseAsync(database, pin with
                    {
                        Mode = AnnMaintenanceMode.Release
                    }, KeyLoad.Clustering.AnnMaintenanceCapabilityKind.Release);
                    await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory)
                        .Where(row => Path.GetFileName(row.Path) == AnnProtocol.IndexFile).ToArray())
                        .IsEquivalentTo(original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    await AnnRetiredReaderAssertions.AssertAsync(database, held, pin, receipt);
                    var image = AnnMaintenanceTestData.Snapshot(database);
                    var cut = database.Store.Position;
                    var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => AnnPublicWholeFlow.Engine(database, runtime)
                        .WaitForAnnIndexAsync(AnnWaitWholeFlow.Root, request, TestContext.Current!.Execution.CancellationToken))
                        ?? throw new InvalidOperationException();
                    await Assert.That(error.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
                    await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
                }, failures);
            }
            finally { if (held is not null) { ServerFailureObserver.Observe(held.Dispose, failures); } }
            ServerFailureObserver.ThrowIfAny(failures);
            await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory)
                .Any(row => Path.GetFileName(row.Path) == AnnProtocol.IndexFile)).IsFalse();
            await runtime.Owner.AbortAsync(runtime.SessionId);
            var next = ConfigureNext(database, pin);
            var began = await runtime.PhaseAsync(database, next, KeyLoad.Clustering.AnnMaintenanceCapabilityKind.Begin);
            _ = await AnnMaintenancePhaseAssertions.FinishAsync(database, runtime, next, began.Source!.ThroughSequence);
            await AnnWaitWholeFlow.HealthyAsync(database, runtime, AnnWaitWholeFlow.Request(database, next, receipt),
                TestContext.Current!.Execution.CancellationToken);
        });

    private static IAnnProjectionLease Acquire(TestDatabase database, AnnMaintenanceTestRuntime runtime, WaitForAnnIndexRequest request)
    {
        var budget = new ReadExecutionBudget(database.Database.EvaluationClock);
        return database.Database.WithQueryView(AnnWaitWholeFlow.Root, request.Partition, request.Collection, (view, principal, resource) =>
        {
            database.Database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
            database.Database.Authorization.RequireFieldUse(principal, resource, request.VectorField);
            return runtime.Owner.Acquire(view, new(request.Partition, request.Collection, request.VectorField, request.Space,
                request.Consumer, request.IndexGeneration), budget);
        });
    }

    private static AnnMaintenanceRequest ConfigureNext(TestDatabase database, AnnMaintenanceRequest original)
    {
        var consumer = new ProjectionConsumerRef(database.Partition, NextConsumer);
        var id = Guid.NewGuid();
        _ = database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, consumer, new(NextGeneration, [], [.. Kinds]), StartAfter), id: id).Get<ProjectionConsumerInfo>();
        return original with
        {
            CommandId = Guid.NewGuid(),
            Consumer = consumer,
            IndexGeneration = NextGeneration
        };
    }
}
