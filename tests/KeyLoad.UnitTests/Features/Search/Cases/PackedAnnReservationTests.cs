using System.Numerics;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnReservationTests
{
    private const int SparseRecordCount = 512;
    private const int SparseDimension = 1_024;
    private const int ConstructionConnections = 4;
    private const int ConstructionEf = 4;
    private const int ConstructionLevel = 1;

    [Test]
    public async Task AcAnn001BuildScratchReportedReservationIsInclusiveAndOneByteBelowFails()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.Cosine;
        var space = PackedAnnTestData.Space(metric, 2);
        PackedAnnIndexTestSupport.PersistVectors(database, space, PackedAnnTestData.Field(metric), [[1, 0]]);
        var records = PackedAnnTestData.Load(database, metric);
        var measured = PackedAnnIndexTestSupport.Build(space, records, new(), PackedAnnIndexTestSupport.Budget(database));
        var retainedReservation = measured.RetainedBytesUpperBound;
        await Assert.That(retainedReservation > 1_024).IsTrue();
        var retainedAtCap = PackedAnnIndexTestSupport.Build(space, records,
            new() { MaxIndexBytes = retainedReservation }, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(retainedAtCap.RetainedBytesUpperBound).IsEqualTo(retainedReservation);
        await AssertBudgetExceeded(() => PackedAnnIndexTestSupport.Build(space, records,
            new() { MaxIndexBytes = retainedReservation - 1 }, PackedAnnIndexTestSupport.Budget(database)));
        var reservation = measured.BuildScratchBytesUpperBound;
        await Assert.That(reservation > 1_024).IsTrue();
        var cappedOptions = new PackedAnnOptions { MaxScratchBytes = reservation };
        var atCap = PackedAnnIndexTestSupport.Build(space, records, cappedOptions,
            PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(atCap.BuildScratchBytesUpperBound).IsEqualTo(reservation);
        await AssertBudgetExceeded(() => PackedAnnIndexTestSupport.Build(space, records,
            cappedOptions with { MaxScratchBytes = reservation - 1 }, PackedAnnIndexTestSupport.Budget(database)));
    }

    [Test]
    public async Task AcAnn003SparseExactSearchReservationUsesActualResultsAndIsInclusive()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, SparseDimension);
        PackedAnnTestData.Seed(database, SparseRecordCount, SparseDimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        await Assert.That(records.Length).IsEqualTo(SparseRecordCount);
        var eligibility = SparseBitmap(records.Length);
        var options = SparseOptions();
        var measuredIndex = PackedAnnIndexTestSupport.Build(space, records, options,
            PackedAnnIndexTestSupport.Budget(database));
        var query = PackedAnnTestData.Vector(810, space.Dimension, PackedAnnTestData.CorpusSeed);
        var measured = measuredIndex.Search(query, SparseRecordCount, eligibility,
            PackedAnnIndexTestSupport.Budget(database));
        var reservation = measured.ScratchBytesUpperBound;
        await Assert.That(reservation > 1_024).IsTrue();
        await Assert.That(measured.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
        await Assert.That(measured.Candidates.Length).IsEqualTo(eligibility.Sum(BitOperations.PopCount));
        await Assert.That(reservation >= measuredIndex.BuildScratchBytesUpperBound).IsTrue();

        var cappedOptions = options with { MaxScratchBytes = reservation };
        var atCapIndex = PackedAnnIndexTestSupport.Build(space, records, cappedOptions,
            PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(atCapIndex.BuildScratchBytesUpperBound <= reservation).IsTrue();
        var atCap = atCapIndex.Search(query, SparseRecordCount, eligibility,
            PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(atCap.ScratchBytesUpperBound).IsEqualTo(reservation);
        await Assert.That(atCap.Candidates.Select(candidate => candidate.DocumentId))
            .IsEquivalentTo(measured.Candidates.Select(candidate => candidate.DocumentId));
        for (var ordinal = 0; ordinal < measured.Candidates.Length; ordinal++)
        {
            await Assert.That(atCap.Candidates[ordinal].DocumentId).IsEqualTo(measured.Candidates[ordinal].DocumentId);
            await Assert.That(atCap.Candidates[ordinal].DocumentRevision).IsEqualTo(measured.Candidates[ordinal].DocumentRevision);
            await Assert.That(atCap.Candidates[ordinal].Score).IsEqualTo(measured.Candidates[ordinal].Score);
        }

        var oneBelow = options with { MaxScratchBytes = reservation - 1 };
        var belowIndex = PackedAnnIndexTestSupport.Build(space, records, oneBelow,
            PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(belowIndex.BuildScratchBytesUpperBound < reservation).IsTrue();
        await AssertBudgetExceeded(() => belowIndex.Search(query, SparseRecordCount, eligibility,
            PackedAnnIndexTestSupport.Budget(database)));
    }

    [Test]
    public async Task AcAnn001NullSpaceAndVectorIdentityFieldsFailTypedValidation()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.Cosine;
        var space = PackedAnnTestData.Space(metric, 2);
        PackedAnnIndexTestSupport.PersistVectors(database, space, PackedAnnTestData.Field(metric), [[1, 0]]);
        var records = PackedAnnTestData.Load(database, metric);
        await AssertValidation(() => PackedAnnIndex.Build(space with { Id = null! }, records, new(),
            PackedAnnIndexTestSupport.Budget(database)));
        await AssertValidation(() => PackedAnnIndex.Build(space, [records[0] with { DocumentId = null! }], new(),
            PackedAnnIndexTestSupport.Budget(database)));
        await AssertValidation(() => PackedAnnIndex.Build(space, [records[0] with { Field = null! }], new(),
            PackedAnnIndexTestSupport.Budget(database)));
    }

    private static PackedAnnOptions SparseOptions()
        => new()
        {
            Connections = ConstructionConnections,
            EfConstruction = ConstructionEf,
            EfSearch = 1,
            MaxLevel = ConstructionLevel,
            ExactThreshold = 128,
            Seed = PackedAnnTestData.CorpusSeed
        };

    private static ulong[] SparseBitmap(int count)
    {
        var words = new ulong[(count + 63) / 64];
        for (var ordinal = 0; ordinal < count; ordinal += 10)
        {
            words[ordinal / 64] |= 1UL << (ordinal % 64);
        }
        return words;
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
