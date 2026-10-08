using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnPublicWholeFlow
{
    internal const int Count = 3;
    internal const int Version = 1;
    private const long Revision = 1;
    private const string Json = "{}";
    private const string ExactThreshold = "KeyLoad:PackedAnn:ExactThreshold";
    private const string SearchBreadth = "KeyLoad:PackedAnn:MaximumSearchBreadth";
    private const string EfSearch = "KeyLoad:PackedAnn:EfSearch";

    internal static async Task RunAsync(int threshold, int breadth,
        Func<TestDatabase, NativeAnnMaintenanceTestRuntime, AnnMaintenanceRequest, Task> operation, TimeProvider? clock = null)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = AnnSeedTestSupport.Create(Count, timeProvider: clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var request = NativeAnnMaintenanceTestData.Pin(database);
                var settings = new Dictionary<string, string?>
                {
                    [ExactThreshold] = threshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [SearchBreadth] = breadth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [EfSearch] = breadth.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                await using var runtime = new NativeAnnMaintenanceTestRuntime(database,
                    configuration: new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var began = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Begin);
                    _ = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, runtime, request, began.Source!.ThroughSequence);
                    await operation(database, runtime, request);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static ApproximateSearchRequest Request(TestDatabase database, AnnMaintenanceRequest pin)
        => new(Version, new(database.Partition, AnnSeedTestSupport.Collection,
            VectorField: AnnSeedTestSupport.Field, Vector: [1, 0, 0], Space: AnnSeedTestSupport.Space(), Limit: Count),
            pin.Consumer, pin.IndexGeneration);

    internal static SearchEngine Engine(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime,
        bool enabled = true, int? bytes = null)
        => new(database.Database, Options.Create(new QueryExecutionOptions
        { EnableApproximateSearch = enabled, MaximumResultBytes = bytes }), null, runtime.Owner);

    internal static async Task PageAsync(AnnSearchPage page, TestDatabase database, long cut, AnnPageMode mode, bool empty = false, bool redacted = false)
    {
        var rows = empty ? [] : new RankedDocument[]
        { Row(database, "seed-00002", 1d / 61d, redacted), Row(database, "seed-00001", 1d / 62d, redacted), Row(database, "seed-00000", 1d / 63d, redacted) };
        var expected = new AnnSearchPage(Version, [.. rows], cut, mode, mode != AnnPageMode.Approximate,
            NativeAnnMaintenanceTestData.Generation, AnnPageMode.Approximate);
        await Assert.That(JsonDefaults.Serialize(page).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static RankedDocument Row(TestDatabase database, string id, double score, bool redacted)
        => new(new(new(database.Partition, AnnSeedTestSupport.Collection, id), Revision, Json, redacted,
            redacted ? [AnnSeedTestSupport.Field] : []), score);

    internal static async Task UnchangedAsync(TestDatabase database, (string Key, string Value)[] image, long cut)
    {
        await Assert.That(NativeAnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(cut);
    }
}
