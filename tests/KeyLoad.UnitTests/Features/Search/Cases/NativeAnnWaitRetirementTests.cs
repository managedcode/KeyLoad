using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnWaitRetirementTests
{
    private const long NextGeneration = 2;
    private const long StartAfter = 0;
    private const string NextConsumer = "ann-wait-next-pin";
    private static readonly string[] Kinds = ["putDocument", "patchDocument", "deleteDocument", "putVector", "applyVectorProjection"];

    [Test]
    public Task ReleasedGenerationKeepsActualPinnedIndexUntilReaderJoinsThenNewNativeGenerationReturnsHealthyPrefix()
        => NativeAnnWaitWholeFlow.RunAsync(async (fixture, runtime, pin, receipt) =>
        {
            var database = fixture.Canonical;
            var request = NativeAnnWaitWholeFlow.Request(database, pin, receipt);
            IAnnProjectionLease? held = null;
            var failures = new List<Exception>();
            try
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    held = Acquire(database, runtime, request);
                    var original = NativeAnnFixtureFileSnapshot.Capture(database.Directory)
                        .Where(row => Path.GetFileName(row.Path) == NativeAnnProtocol.IndexFile).ToArray();
                    await Assert.That(original).IsNotEmpty();
                    _ = AnnProjectionPinTestSupport.Release(database, pin.Consumer, generation: pin.IndexGeneration).Get<ProjectionConsumerInfo>();
                    _ = await runtime.PhaseAsync(database, pin with { Mode = AnnMaintenanceMode.Release }, KeyLoad.Orleans.AnnMaintenanceCapabilityKind.Release);
                    await Assert.That(NativeAnnFixtureFileSnapshot.Capture(database.Directory)
                        .Where(row => Path.GetFileName(row.Path) == NativeAnnProtocol.IndexFile).ToArray())
                        .IsEquivalentTo(original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    var image = NativeAnnMaintenanceTestData.Snapshot(database);
                    var cut = database.Store.Position;
                    var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => AnnPublicWholeFlow.Engine(database, runtime)
                        .WaitForAnnIndexAsync(NativeAnnWaitWholeFlow.Root, request, TestContext.Current!.Execution.CancellationToken))
                        ?? throw new InvalidOperationException();
                    await Assert.That(error.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
                    await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
                }, failures);
            }
            finally { if (held is not null) { ServerFailureObserver.Observe(held.Dispose, failures); } }
            ServerFailureObserver.ThrowIfAny(failures);
            await Assert.That(NativeAnnFixtureFileSnapshot.Capture(database.Directory)
                .Any(row => Path.GetFileName(row.Path) == NativeAnnProtocol.IndexFile)).IsFalse();
            await runtime.Owner.AbortAsync(runtime.SessionId);
            var next = ConfigureNext(database, pin);
            var began = await runtime.PhaseAsync(database, next, KeyLoad.Orleans.AnnMaintenanceCapabilityKind.Begin);
            _ = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, runtime, next, began.Source!.ThroughSequence);
            await NativeAnnWaitWholeFlow.HealthyAsync(database, runtime, NativeAnnWaitWholeFlow.Request(database, next, receipt),
                TestContext.Current!.Execution.CancellationToken);
        });

    private static IAnnProjectionLease Acquire(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime, WaitForAnnIndexRequest request)
    {
        var budget = new ReadExecutionBudget(database.Database.OperationLimitsOptions, database.Database.EvaluationClock);
        return database.Database.WithQueryView(NativeAnnWaitWholeFlow.Root, request.Partition, request.Collection, (view, principal, resource) =>
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
        return original with { CommandId = Guid.NewGuid(), Consumer = consumer, IndexGeneration = NextGeneration };
    }
}
