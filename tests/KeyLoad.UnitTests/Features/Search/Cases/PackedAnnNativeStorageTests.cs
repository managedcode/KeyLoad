using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnNativeStorageTests
{
    private const string OriginalFile = "packed-original.bin";
    private const string RestoredFile = "packed-restored.bin";
    private const int First = 0;
    private const int Step = 1;
    private const int ResultCount = 3;
    private const int Dimension = 2;
    private const int BoundaryCount = 64;
    private const int BoundaryDimension = 8;
    private const int SnapshotBound = 1_024;
    private const int DigestBit = 1;
    private const long MinimumFileBytes = 1_024;
    private static readonly float[] Query = [1, 0];
    private static readonly string[] LiteralIds = ["record-00000", "record-00001", "record-00002"];
    private static readonly double[] LiteralScores = [1, 0, -1];

    [Test]
    public async Task NativeSaveLoadRestoresLiteralScoresExactGraphBytesAndOriginalWorkCounters()
    {
        using var db = new TestDatabase();
        SeedLiteral(db);
        var policy = new PackedAnnOptions { ExactThreshold = First };
        var index = Build(db, policy);
        var storage = Options.Create(new PackedAnnStorageOptions());
        var before = Snapshot(db);
        var position = db.Store.Position;
        using var originalFile = Open(db, OriginalFile);
        var originalDigest = index.Save(originalFile, storage, PackedAnnIndexTestSupport.Budget(db));
        await originalFile.FlushAsync(TestContext.Current!.Execution.CancellationToken);
        RandomAccess.FlushToDisk(originalFile.SafeFileHandle);
        var restored = PackedAnnIndex.Load(originalFile, originalDigest, UnitExecutionOptions.PackedAnn(policy),
            storage, PackedAnnIndexTestSupport.Budget(db));
        using var restoredFile = Open(db, RestoredFile);
        var restoredDigest = restored.Save(restoredFile, storage, PackedAnnIndexTestSupport.Budget(db));
        await Assert.That(restoredDigest).IsEquivalentTo(originalDigest, CollectionOrdering.Matching);
        var originalBytes = await File.ReadAllBytesAsync(originalFile.Name, TestContext.Current!.Execution.CancellationToken);
        var restoredBytes = await File.ReadAllBytesAsync(restoredFile.Name, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(originalBytes).IsEquivalentTo(restoredBytes, CollectionOrdering.Matching);
        await AssertResultsAsync(db, index, restored);
        await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task CorruptAndWrongProfileNativeLoadsKeepOriginalCanonicalAndIndexHealthy()
    {
        using var db = new TestDatabase();
        SeedLiteral(db);
        var policy = new PackedAnnOptions { ExactThreshold = First };
        var index = Build(db, policy);
        var storage = Options.Create(new PackedAnnStorageOptions());
        using var file = Open(db, OriginalFile);
        var digest = index.Save(file, storage, PackedAnnIndexTestSupport.Budget(db));
        var bytes = await File.ReadAllBytesAsync(file.Name, TestContext.Current!.Execution.CancellationToken);
        var before = Snapshot(db);
        var position = db.Store.Position;
        var corrupt = bytes.ToArray();
        corrupt[^Step] ^= DigestBit;
        using var corruptFile = new MemoryStream(corrupt, writable: false);
        var damaged = Assert.ThrowsExactly<KeyLoadException>(() => PackedAnnIndex.Load(corruptFile, digest,
            UnitExecutionOptions.PackedAnn(policy), storage, PackedAnnIndexTestSupport.Budget(db)));
        await Assert.That(damaged.Code).IsEqualTo(ErrorCode.Corruption);
        using var truncatedFile = new MemoryStream(bytes.AsSpan(First, bytes.Length - Step).ToArray(), writable: false);
        var truncated = Assert.ThrowsExactly<KeyLoadException>(() => PackedAnnIndex.Load(truncatedFile, digest,
            UnitExecutionOptions.PackedAnn(policy), storage, PackedAnnIndexTestSupport.Budget(db)));
        await Assert.That(truncated.Code).IsEqualTo(ErrorCode.Corruption);
        var wrongProfile = Assert.ThrowsExactly<KeyLoadException>(() => PackedAnnIndex.Load(file, digest,
            UnitExecutionOptions.PackedAnn(policy with { Seed = policy.Seed + Step }), storage, PackedAnnIndexTestSupport.Budget(db)));
        await Assert.That(wrongProfile.Code).IsEqualTo(ErrorCode.Corruption);
        var healthy = PackedAnnIndex.Load(file, digest, UnitExecutionOptions.PackedAnn(policy), storage, PackedAnnIndexTestSupport.Budget(db));
        await AssertResultsAsync(db, index, healthy);
        await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        var healthyBytes = await File.ReadAllBytesAsync(file.Name, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(healthyBytes).IsEquivalentTo(bytes, CollectionOrdering.Matching);
    }

    [Test]
    public async Task NativeFileBoundIsInclusiveAndCancellationSettlesBeforeHealthyRestore()
    {
        using var db = new TestDatabase();
        PackedAnnTestData.Configure(db);
        PackedAnnTestData.Seed(db, BoundaryCount, BoundaryDimension, DistanceMetric.DotProduct);
        var before = Snapshot(db);
        var position = db.Store.Position;
        var policy = new PackedAnnOptions { ExactThreshold = First };
        var index = Build(db, policy);
        using var file = Open(db, OriginalFile);
        var storage = Options.Create(new PackedAnnStorageOptions());
        var digest = index.Save(file, storage, PackedAnnIndexTestSupport.Budget(db));
        await Assert.That(file.Length).IsGreaterThan(MinimumFileBytes);
        using var exact = Open(db, RestoredFile);
        var bounded = Options.Create(storage.Value with { MaxFileBytes = file.Length });
        await Assert.That(index.Save(exact, bounded, PackedAnnIndexTestSupport.Budget(db))).IsEquivalentTo(digest, CollectionOrdering.Matching);
        using var below = new MemoryStream();
        var excess = Assert.ThrowsExactly<KeyLoadException>(() => index.Save(below,
            Options.Create(storage.Value with { MaxFileBytes = file.Length - Step }), PackedAnnIndexTestSupport.Budget(db)));
        await Assert.That(excess.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => PackedAnnIndex.Load(file, digest,
            UnitExecutionOptions.PackedAnn(policy), storage, PackedAnnIndexTestSupport.Budget(db, token: cancelled.Token)));
        await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        var restored = PackedAnnIndex.Load(file, digest, UnitExecutionOptions.PackedAnn(policy), bounded, PackedAnnIndexTestSupport.Budget(db));
        var query = PackedAnnTestData.Vector(First, BoundaryDimension, PackedAnnTestData.CorpusSeed);
        var expected = index.Search(query, ResultCount, null, PackedAnnIndexTestSupport.Budget(db));
        var actual = restored.Search(query, ResultCount, null, PackedAnnIndexTestSupport.Budget(db));
        await Assert.That(actual.Candidates).IsEquivalentTo(expected.Candidates, CollectionOrdering.Matching);
        await Assert.That(actual.WorkUnits).IsEqualTo(expected.WorkUnits);
        await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
    }

    private static void SeedLiteral(TestDatabase db)
    {
        PackedAnnTestData.Configure(db);
        PackedAnnIndexTestSupport.PersistVectors(db, PackedAnnTestData.Space(DistanceMetric.DotProduct, Dimension),
            PackedAnnTestData.Field(DistanceMetric.DotProduct), [[1, 0], [0, 1], [-1, 0]]);
    }

    private static PackedAnnIndex Build(TestDatabase db, PackedAnnOptions policy)
    {
        var records = PackedAnnTestData.Load(db, DistanceMetric.DotProduct);
        return PackedAnnIndexTestSupport.Build(PackedAnnTestData.Space(DistanceMetric.DotProduct,
            records.First().Values.Length), records, policy, PackedAnnIndexTestSupport.Budget(db));
    }

    private static FileStream Open(TestDatabase db, string name)
        => new(Path.Combine(db.Directory, name), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);

    private static async Task AssertResultsAsync(TestDatabase db, PackedAnnIndex index, PackedAnnIndex restored)
    {
        var expected = index.Search(Query, ResultCount, null, PackedAnnIndexTestSupport.Budget(db));
        var actual = restored.Search(Query, ResultCount, null, PackedAnnIndexTestSupport.Budget(db));
        await Assert.That(actual.Candidates.Select(row => row.DocumentId).ToArray()).IsEquivalentTo(LiteralIds, CollectionOrdering.Matching);
        await Assert.That(actual.Candidates.Select(row => row.Score).ToArray()).IsEquivalentTo(LiteralScores, CollectionOrdering.Matching);
        await Assert.That(actual.Candidates.Select(row => row.DocumentRevision).ToArray()).IsEquivalentTo([1L, 1L, 1L], CollectionOrdering.Matching);
        await Assert.That(actual.Candidates).IsEquivalentTo(expected.Candidates, CollectionOrdering.Matching);
        await Assert.That(actual.Mode).IsEqualTo(expected.Mode);
        await Assert.That(actual.WorkUnits).IsEqualTo(expected.WorkUnits);
        await Assert.That(actual.DistanceEvaluations).IsEqualTo(expected.DistanceEvaluations);
        await Assert.That(actual.EdgeVisits).IsEqualTo(expected.EdgeVisits);
        await Assert.That(actual.ScratchBytesUpperBound).IsEqualTo(expected.ScratchBytesUpperBound);
    }

    private static (string Key, string Value)[] Snapshot(TestDatabase db) => db.Store.Read(view =>
    {
        var page = view.Scan([], SnapshotBound);
        if (page.HasMore)
        { throw new InvalidOperationException("The native ANN storage fixture exceeds its complete snapshot bound."); }
        return page.Records.Select(row => (Convert.ToHexString(row.Key.Span), Convert.ToHexString(row.Value.Span))).ToArray();
    });
}
