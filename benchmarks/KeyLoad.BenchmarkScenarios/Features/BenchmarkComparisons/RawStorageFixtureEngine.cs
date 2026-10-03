using Tsavorite.core;
using RawAllocator = Tsavorite.core.SpanByteAllocator<Tsavorite.core.StoreFunctions<Tsavorite.core.SpanByteComparer, Tsavorite.core.DefaultRecordTriggers>>;
using RawStore = Tsavorite.core.TsavoriteKV<Tsavorite.core.StoreFunctions<Tsavorite.core.SpanByteComparer, Tsavorite.core.DefaultRecordTriggers>, Tsavorite.core.SpanByteAllocator<Tsavorite.core.StoreFunctions<Tsavorite.core.SpanByteComparer, Tsavorite.core.DefaultRecordTriggers>>>;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal interface IRawStorageFixtureEngine : IDisposable
{
    string? Directory { get; }

    bool TryRead(int index, out ReadOnlyMemory<byte> value);

    void Upsert(int index, bool alternate);

    bool Delete(int index);
}

internal static class RawStorageFixtureEngineFactory
{
    internal static IRawStorageFixtureEngine Create(RawStorageEngineKind engine, RawStorageCorpus corpus,
        byte[] readScratch)
        => engine switch
        {
            RawStorageEngineKind.ZoneTree => new RawStorageFixtureZoneTreeEngine(corpus, readScratch),
            RawStorageEngineKind.Tsavorite => new RawStorageFixtureTsavoriteEngine(corpus, readScratch),
            _ => throw new ArgumentOutOfRangeException(nameof(engine))
        };
}

internal static class RawStorageFixtureTsavoriteSettings
{
    private const int IndexBytes = 4 * 1024 * 1024;
    private const int PageBytes = 1024 * 1024;
    private const int SegmentBytes = 128 * 1024 * 1024;
    private const int LogMemoryBytes = 128 * 1024 * 1024;
    private const double MutableFraction = 0.9;

    internal static KVSettings Create()
        => new(baseDir: null)
        {
            IndexSize = IndexBytes,
            PageSize = PageBytes,
            SegmentSize = SegmentBytes,
            LogMemorySize = LogMemoryBytes,
            MutableFraction = MutableFraction,
            ReadCacheEnabled = false
        };

    internal static RawStore CreateStore(KVSettings settings)
    {
        var functions = StoreFunctions.Create();
        return new RawStore(settings, functions,
            static (allocatorSettings, storeFunctions) => new RawAllocator(allocatorSettings, storeFunctions));
    }
}
