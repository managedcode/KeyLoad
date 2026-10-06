using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnValidationTests
{
    [Test]
    public async Task AcAnn001InvalidOptionsAndSpacesFailTypedValidation()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, 1);
        var records = SeedRecords(database, metric, space);
        var budget = PackedAnnIndexTestSupport.Budget(database);
        PackedAnnOptions[] invalid =
        [
            new() { Connections = 3 }, new() { EfConstruction = 15 }, new() { EfConstruction = 4_097 },
            new() { EfSearch = 0 }, new() { MaxLevel = 0 }, new() { ExactThreshold = 4_097 },
            new() { MaxRecords = 0 }, new() { MaxIndexBytes = 1_023 }, new() { MaxScratchBytes = 1_023 }
        ];
        foreach (var options in invalid)
        {
            await AssertValidation(() => PackedAnnIndex.Build(space, records, Microsoft.Extensions.Options.Options.Create(options), budget));
        }
        await AssertValidation(() => PackedAnnIndex.Build(space with { Dimension = 0 }, records, UnitExecutionOptions.PackedAnn(new()), budget));
        await AssertValidation(() => PackedAnnIndex.Build(space with { Dimension = 4_097 }, records, UnitExecutionOptions.PackedAnn(new()), budget));
        await AssertValidation(() => PackedAnnIndex.Build(space with { Metric = (DistanceMetric)int.MaxValue }, records, UnitExecutionOptions.PackedAnn(new()), budget));
        await AssertValidation(() => PackedAnnIndex.Build(space with { Id = "\u0001bad" }, records, UnitExecutionOptions.PackedAnn(new()), budget));
    }

    [Test]
    public async Task AcAnn001InvalidCanonicalRecordsAndOrderingFailTypedValidation()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var space = PackedAnnTestData.Space(DistanceMetric.Cosine, 1);
        var records = SeedRecords(database, DistanceMetric.Cosine, space);
        var valid = records[0];
        VectorRecord[] malformed =
        [
            valid with { DocumentId = string.Empty }, valid with { Field = string.Empty },
            valid with { DocumentRevision = 0 }, valid with { Space = space with { Dimension = 2 } },
            valid with { Values = default }, valid with { Values = ImmutableArray.Create(float.NaN) },
            valid with { Values = ImmutableArray.Create(float.PositiveInfinity) }
        ];
        foreach (var record in malformed)
        {
            await AssertValidation(() => PackedAnnIndex.Build(space, [record], UnitExecutionOptions.PackedAnn(new()), PackedAnnIndexTestSupport.Budget(database)));
        }
        await AssertValidation(() => PackedAnnIndex.Build(space,
[valid, records[1] with { Field = "another-field" }], UnitExecutionOptions.PackedAnn(new()), PackedAnnIndexTestSupport.Budget(database)));
        await AssertValidation(() => PackedAnnIndex.Build(space, [records[1], records[0]], UnitExecutionOptions.PackedAnn(new()), PackedAnnIndexTestSupport.Budget(database)));
        await AssertValidation(() => PackedAnnIndex.Build(space, [records[0], records[0]], UnitExecutionOptions.PackedAnn(new()), PackedAnnIndexTestSupport.Budget(database)));
    }

    [Test]
    public async Task AcAnn001SmallActualStoreIndexFitsThirtyTwoKibIndexAndFourKibScratchCaps()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, 1);
        PackedAnnIndexTestSupport.PersistVectors(database, space, PackedAnnTestData.Field(metric), [[1f]]);
        var records = PackedAnnTestData.Load(database, metric);
        var bounded = new PackedAnnOptions
        {
            Connections = 4,
            EfConstruction = 4,
            EfSearch = 1,
            MaxRecords = 1,
            MaxIndexBytes = 32_768,
            MaxScratchBytes = 4_096
        };
        var index = PackedAnnIndex.Build(space, records, UnitExecutionOptions.PackedAnn(bounded), PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(index.Count).IsEqualTo(1);
        await Assert.That(index.RetainedBytesUpperBound <= bounded.MaxIndexBytes).IsTrue();
        var result = index.Search(new float[] { 1f }, 1, null, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(result.Candidates.Single().DocumentId).IsEqualTo(records[0].DocumentId);
    }

    [Test]
    public async Task AcAnn001PositiveCountAndIndexOrScratchExcessReturnBudgetExceeded()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.Cosine;
        var space = PackedAnnTestData.Space(metric, 4_096);
        PackedAnnIndexTestSupport.PersistVectors(database, space, PackedAnnTestData.Field(metric),
            [new float[4_096], new float[4_096]]);
        var records = PackedAnnTestData.Load(database, metric);
        await AssertBudgetExceeded(() => PackedAnnIndex.Build(space, records,
            UnitExecutionOptions.PackedAnn(new() { MaxRecords = 1 }), PackedAnnIndexTestSupport.Budget(database)));
        await AssertBudgetExceeded(() => PackedAnnIndex.Build(space, records,
            UnitExecutionOptions.PackedAnn(new() { MaxIndexBytes = 1_024 }), PackedAnnIndexTestSupport.Budget(database)));
        var queryScratchLimited = new PackedAnnOptions
        {
            Connections = 4,
            EfConstruction = 4,
            EfSearch = 1,
            ExactThreshold = 0,
            MaxRecords = 2,
            MaxIndexBytes = 1_000_000,
            MaxScratchBytes = 4_096
        };
        var index = PackedAnnIndex.Build(space, records, UnitExecutionOptions.PackedAnn(queryScratchLimited), PackedAnnIndexTestSupport.Budget(database));
        await AssertBudgetExceeded(() => index.Search(new float[4_096], 1, null,
            PackedAnnIndexTestSupport.Budget(database)));
    }

    [Test]
    public async Task AcAnn003AlreadyCanceledBuildAndSearchStopOnTheirRealExecutionBudgets()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.Cosine;
        var space = PackedAnnTestData.Space(metric, 2);
        PackedAnnTestData.Seed(database, 4, space.Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var options = new PackedAnnOptions();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var canceledBuildBudget = new AnnWorkBudget(new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            TimeProvider.System, cancellation.Token), PackedAnnIndexTestSupport.GenerousWorkLimit);
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            PackedAnnIndex.Build(space, records, UnitExecutionOptions.PackedAnn(options), canceledBuildBudget));

        var index = PackedAnnIndex.Build(space, records, UnitExecutionOptions.PackedAnn(options), PackedAnnIndexTestSupport.Budget(database));
        var canceledSearchBudget = new AnnWorkBudget(new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            TimeProvider.System, cancellation.Token), PackedAnnIndexTestSupport.GenerousWorkLimit);
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            index.Search(new float[] { 1f, 0f }, 1, null, canceledSearchBudget));
    }

    [Test]
    public async Task AcAnn002InvalidQueryIsRejectedForEmptySourceAndEligibility()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var records = PackedAnnTestData.Load(database, metric);
        var index = PackedAnnIndex.Build(PackedAnnTestData.Space(metric, 1), records, UnitExecutionOptions.PackedAnn(new()),
            PackedAnnIndexTestSupport.Budget(database));
        await AssertValidation(() => index.Search(new float[] { float.NaN }, 1, null,
            PackedAnnIndexTestSupport.Budget(database)));
        await AssertValidation(() => index.Search(new float[] { 1f }, 1, new ulong[] { 1 },
            PackedAnnIndexTestSupport.Budget(database)));

        var populated = SeedRecords(database, metric, PackedAnnTestData.Space(metric, 1));
        var populatedIndex = PackedAnnIndex.Build(PackedAnnTestData.Space(metric, 1), populated, UnitExecutionOptions.PackedAnn(new()),
            PackedAnnIndexTestSupport.Budget(database));
        var noEligible = new ulong[] { 0 };
        await AssertValidation(() => populatedIndex.Search(new float[2], 1, noEligible,
            PackedAnnIndexTestSupport.Budget(database)));
        await AssertValidation(() => populatedIndex.Search(new float[] { 1f }, 0, noEligible,
            PackedAnnIndexTestSupport.Budget(database)));
        await AssertValidation(() => populatedIndex.Search(new float[] { float.NaN }, 1, noEligible,
            PackedAnnIndexTestSupport.Budget(database)));
    }

    private static VectorRecord[] SeedRecords(TestDatabase database, DistanceMetric metric, VectorSpace space)
    {
        PackedAnnIndexTestSupport.PersistVectors(database, space, PackedAnnTestData.Field(metric), [[1f], [2f]]);
        return PackedAnnTestData.Load(database, metric);
    }

    private static async Task AssertValidation(Action operation)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }

    private static async Task AssertBudgetExceeded(Action operation)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
