using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnPublicProfileFlow
{
    private const long Generation = 1;
    private const long Start = 0;
    private const long Revision = 1;
    private const int First = 0;
    private const int Count = 3;
    private const string ConsumerName = "ann-public-profile";
    private static readonly string[] Kinds = ["putDocument", "patchDocument", "deleteDocument", "putVector", "applyVectorProjection"];

    internal static async Task RunAsync(bool configuredProfile,
        Func<TestDatabase, NativeAnnMaintenanceTestRuntime, AnnMaintenanceRequest, CommitReceipt, Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new CanonicalReadCutFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = fixture.Canonical;
                database.Configure(AnnSeedTestSupport.Collection, ResourceKind.Collection);
                var receipt = await SeedAsync(fixture, TestContext.Current!.Execution.CancellationToken);
                var (pin, configured) = Pin(database, configuredProfile);
                await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var principal = database.Store.Read(view => database.Database.Principal(view,
                        NativeAnnWaitWholeFlow.Root, database.Database.EvaluationClock.GetUtcNow()));
                    await Assert.That(await runtime.Owner.AdmitOriginalAsync(principal, pin, configured,
                        TestContext.Current!.Execution.CancellationToken)).IsNull();
                    var began = await runtime.PhaseAsync(database, pin, AnnMaintenanceCapabilityKind.Begin);
                    _ = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, runtime, pin,
                        began.Source!.ThroughSequence);
                    await operation(database, runtime, pin, receipt);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<CommitReceipt> SeedAsync(CanonicalReadCutFixture fixture, CancellationToken token)
    {
        var mutations = new List<Mutation>();
        for (var index = First; index < Count; index++)
        {
            var id = AnnSeedTestSupport.Id(index);
            mutations.Add(new PutDocument(AnnSeedTestSupport.Collection, id, "{}"));
            mutations.Add(new PutVector(AnnSeedTestSupport.Collection, id, AnnSeedTestSupport.Field,
                [index, 1, 0], AnnSeedTestSupport.Space(), Revision));
        }
        return NativeAnnWaitWholeFlow.CommitAsync(fixture, [.. mutations], token);
    }

    private static (AnnMaintenanceRequest Pin, ProjectionConsumerInfo Configured) Pin(TestDatabase database, bool enabled)
    {
        AnnProjectionMaintenanceProfile? profile = enabled
            ? new(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Field, AnnSeedTestSupport.Space()) : null;
        var consumer = AnnProjectionPinTestSupport.Consumer(database, ConsumerName);
        var definition = new ProjectionConsumerDefinition(Generation, [], [.. Kinds]) { AnnMaintenance = profile };
        var id = Guid.NewGuid();
        var configured = database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, consumer, definition, StartAfter: Start), id: id)
            .Get<ProjectionConsumerInfo>();
        var actual = database.Database.ReadAtomicPartitionPlacement(NativeAnnWaitWholeFlow.Root,
            database.Partition);
        var placement = new PhysicalShardRecord(actual.PhysicalShardId, actual.Incarnation);
        var pin = new AnnMaintenanceRequest(Guid.NewGuid(), consumer, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Field, AnnSeedTestSupport.Space(), Generation, database.Store.Identity.NodeId,
            placement, AnnMaintenanceMode.Build)
        {
            MaintenanceProfile = profile
        };
        return (pin, configured);
    }

    internal static async Task HealthyAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest pin, CommitReceipt receipt)
    {
        var image = NativeAnnMaintenanceTestData.Snapshot(database);
        var cut = database.Store.Position;
        var token = TestContext.Current!.Execution.CancellationToken;
        var page = await AnnPublicWholeFlow.Engine(database, runtime).ApproximateSearchAsync(
            NativeAnnWaitWholeFlow.Root, AnnPublicWholeFlow.Request(database, pin), token);
        await AnnPublicWholeFlow.PageAsync(page, database, cut, AnnPageMode.Exact);
        await NativeAnnWaitWholeFlow.HealthyAsync(database, runtime,
            NativeAnnWaitWholeFlow.Request(database, pin, receipt), token);
        await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
    }

    internal static async Task RequireDefinitionConflictAsync(TestDatabase database, AnnMaintenanceRequest pin)
    {
        var original = AnnProjectionPinTestSupport.StoredConsumer(database, pin.Consumer);
        var profile = pin.MaintenanceProfile ?? throw new InvalidOperationException("The actual configured profile is absent.");
        var changed = profile with
        {
            Space = profile.Space with
            {
                Version = "v2"
            }
        };
        var definition = new ProjectionConsumerDefinition(Generation, [], [.. Kinds]) { AnnMaintenance = changed };
        var id = Guid.NewGuid();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, pin.Consumer, definition), id: id)
            .Get<ProjectionConsumerInfo>());
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(AnnProjectionPinTestSupport.StoredConsumer(database, pin.Consumer).AsSpan()
            .SequenceEqual(original)).IsTrue();
    }
}
