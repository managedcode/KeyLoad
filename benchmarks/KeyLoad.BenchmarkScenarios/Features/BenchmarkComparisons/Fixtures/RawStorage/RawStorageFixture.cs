using System.Runtime.ExceptionServices;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Owns one non-concurrent raw ZoneTree fixture and its exact binary corpus.</summary>
internal sealed class RawStorageFixture : IDisposable
{
    private const int MinimumMaximumWrites = 1;
    private const int MaximumWritesLimit = 65_536;
    private const int MaximumRecordCount = 4096;
    private const int SmallPayloadBytes = 32;
    private const int LargePayloadBytes = 1024;
    private const string UnavailableMessage = "The raw storage fixture cannot continue after an incomplete native operation.";
    private readonly int maximumWrites;
    private readonly byte[] readScratch;
    private RawStorageFixtureZoneTreeEngine? engine;
    private bool disposed;
    private bool faulted;
    private int attemptedWrites;

    /// <summary>Creates the real ZoneTree engine after validating all caller-supplied bounds.</summary>
    public RawStorageFixture(int recordCount, int valueBytes,
        int maximumWrites = MaximumWritesLimit)
    {
        ValidateArguments(recordCount, valueBytes, maximumWrites);
        this.maximumWrites = maximumWrites;
        Corpus = new(recordCount, valueBytes);
        readScratch = GC.AllocateArray<byte>(valueBytes, pinned: true);
        try
        {
            engine = new RawStorageFixtureZoneTreeEngine(Corpus, readScratch);
            SeedCorpus();
        }
        catch (Exception primary)
        {
            DisposeAfterFailure(primary);
            throw;
        }
    }

    /// <summary>Gets the immutable corpus retained for the fixture lifetime.</summary>
    public RawStorageCorpus Corpus { get; }

    /// <summary>Gets the real ZoneTree directory.</summary>
    public string? Directory => engine?.Directory;

    /// <summary>Reads a seeded or reserved index into the one caller-pinned session scratch buffer.</summary>
    public bool TryRead(int index, out ReadOnlyMemory<byte> value)
    {
        EnsureUsable(index);
        try
        {
            return engine!.TryRead(index, out value);
        }
        catch (Exception)
        {
            faulted = true;
            throw;
        }
    }

    /// <summary>Writes one of the two preallocated immutable values and charges the attempt before the engine call.</summary>
    public void Upsert(int index, bool alternate = false)
    {
        EnsureUsable(index);
        ConsumeWriteQuota();
        try
        {
            engine!.Upsert(index, alternate);
        }
        catch (Exception)
        {
            faulted = true;
            throw;
        }
    }

    /// <summary>Writes a native tombstone or delete and charges even a no-op attempt.</summary>
    public bool Delete(int index)
    {
        EnsureUsable(index);
        ConsumeWriteQuota();
        try
        {
            return engine!.Delete(index);
        }
        catch (Exception)
        {
            faulted = true;
            throw;
        }
    }

    /// <summary>Closes actual native owners in dependency order; repeated calls retry only owners not yet released.</summary>
    public void Dispose()
    {
        disposed = true;
        var active = engine;
        if (active is null)
        {
            GC.SuppressFinalize(this);
            return;
        }

        active.Dispose();
        engine = null;
        GC.SuppressFinalize(this);
    }

    private static void ValidateArguments(int recordCount, int valueBytes, int maximumWrites)
    {
        if (recordCount is < 1 or > MaximumRecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        }

        if (valueBytes is not (SmallPayloadBytes or LargePayloadBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(valueBytes));
        }

        if (maximumWrites is < MinimumMaximumWrites or > MaximumWritesLimit || maximumWrites < recordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWrites));
        }
    }

    private void SeedCorpus()
    {
        for (var index = 0; index < Corpus.RecordCount; index++)
        {
            Upsert(index);
        }
    }

    private void ConsumeWriteQuota()
    {
        if (attemptedWrites >= maximumWrites)
        {
            throw new InvalidOperationException(UnavailableMessage);
        }

        attemptedWrites++;
    }

    private void EnsureUsable(int index)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (faulted)
        {
            throw new InvalidOperationException(UnavailableMessage);
        }

        if ((uint)index >= (uint)(Corpus.RecordCount + 2))
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private void DisposeAfterFailure(Exception primary)
    {
        if (engine is null)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
            return;
        }

        try
        {
            engine.Dispose();
            engine = null;
        }
        catch (Exception cleanup)
        {
            throw new AggregateException(primary, cleanup);
        }

        ExceptionDispatchInfo.Capture(primary).Throw();
    }
}
