using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Verifies fixture validation, bounded writes, input ownership and close behavior.</summary>
[NotInParallel]
internal sealed class RawStorageLifetimeTests
{
    private const int ValidRecordCount = 2;
    private const int ValidPayloadBytes = 32;
    private const int MaximumWrites = 65_536;

    [Test]
    public async Task AcGe003RejectsInvalidEngineCorpusPayloadAndWriteBudgets()
    {
        await Assert.That(() => CreateFixture((RawStorageEngineKind)99, ValidRecordCount, ValidPayloadBytes, MaximumWrites))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateFixture(RawStorageEngineKind.ZoneTree, 0, ValidPayloadBytes, MaximumWrites))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateFixture(RawStorageEngineKind.ZoneTree, 4097, ValidPayloadBytes, MaximumWrites))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateFixture(RawStorageEngineKind.ZoneTree, ValidRecordCount, 16, MaximumWrites))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateFixture(RawStorageEngineKind.ZoneTree, ValidRecordCount, ValidPayloadBytes, 0))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateFixture(RawStorageEngineKind.ZoneTree, ValidRecordCount, ValidPayloadBytes, 1))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateFixture(RawStorageEngineKind.ZoneTree, ValidRecordCount, ValidPayloadBytes,
            MaximumWrites + 1)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(RawStorageEngineKind.ZoneTree)]
    [Arguments(RawStorageEngineKind.Tsavorite)]
    public async Task AcGe003SeedConsumesQuotaAndExcessMutationsLeaveDataUnchanged(RawStorageEngineKind engine)
    {
        using var fixture = new RawStorageFixture(engine, 1, ValidPayloadBytes, maximumWrites: 2);
        var alternate = fixture.Corpus.Value(0, alternate: true).ToArray();

        await Assert.That(fixture.TryRead(0, out _)).IsTrue();
        fixture.Upsert(0, alternate: true);
        await Assert.That(() => fixture.Delete(0)).Throws<InvalidOperationException>();
        await Assert.That(() => fixture.Upsert(1)).Throws<InvalidOperationException>();
        await Assert.That(fixture.TryRead(1, out _)).IsFalse();
        await Assert.That(fixture.TryRead(0, out var after)).IsTrue();
        await Assert.That(after.ToArray().AsSpan().SequenceEqual(alternate)).IsTrue();
    }

    [Test]
    [Arguments(RawStorageEngineKind.ZoneTree)]
    [Arguments(RawStorageEngineKind.Tsavorite)]
    public async Task AcGe003OutOfRangeIndicesFailWithoutChangingSeededRecords(RawStorageEngineKind engine)
    {
        using var fixture = new RawStorageFixture(engine, ValidRecordCount, ValidPayloadBytes, MaximumWrites);
        var original = fixture.Corpus.Value(0).ToArray();

        await Assert.That(() => fixture.TryRead(-1, out _)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => fixture.TryRead(ValidRecordCount + 2, out _)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => fixture.Upsert(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => fixture.Upsert(ValidRecordCount + 2)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => fixture.Delete(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => fixture.Delete(ValidRecordCount + 2)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(fixture.TryRead(0, out var actual)).IsTrue();
        await Assert.That(actual.ToArray().AsSpan().SequenceEqual(original)).IsTrue();
    }

    [Test]
    [Arguments(RawStorageEngineKind.ZoneTree)]
    [Arguments(RawStorageEngineKind.Tsavorite)]
    public async Task AcGe003DisposedFixtureRejectsOperationsAndCleanupRepeats(RawStorageEngineKind engine)
    {
        using var fixture = new RawStorageFixture(engine, ValidRecordCount, ValidPayloadBytes, MaximumWrites);
        var storageDirectory = fixture.Directory;
        if (engine == RawStorageEngineKind.ZoneTree)
        {
            await Assert.That(storageDirectory is not null).IsTrue();
            await Assert.That(System.IO.Directory.Exists(storageDirectory)).IsTrue();
        }
        else
        {
            await Assert.That(storageDirectory).IsNull();
        }

        fixture.Dispose();
        if (storageDirectory is not null)
        {
            await Assert.That(System.IO.Directory.Exists(storageDirectory)).IsFalse();
        }
        else
        {
            await Assert.That(fixture.Directory).IsNull();
        }
        fixture.Dispose();

        await Assert.That(() => fixture.TryRead(0, out _)).Throws<ObjectDisposedException>();
        await Assert.That(() => fixture.Upsert(0)).Throws<ObjectDisposedException>();
        await Assert.That(() => fixture.Delete(0)).Throws<ObjectDisposedException>();
        fixture.Dispose();
    }

    private static RawStorageFixture CreateFixture(RawStorageEngineKind engine, int recordCount, int payloadBytes,
        int maximumWrites)
        => new(engine, recordCount, payloadBytes, maximumWrites);
}
