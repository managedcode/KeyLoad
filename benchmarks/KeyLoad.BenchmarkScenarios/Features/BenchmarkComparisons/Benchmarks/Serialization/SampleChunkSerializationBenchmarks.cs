using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Measures bounded lossless chunk values against the actual native per-record codec; these are small development controls.</summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 8, id: JobIdentity)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SampleChunkSerializationBenchmarks
{
    private const string JobIdentity = "SampleChunkCodecDevelopment";
    private const string EncodeCategory = "Encode";
    private const string DecodeCategory = "Decode";
    private const int DefaultControlRecordCount = 32;
    private const string RequireStateFailureMessage = "The sample chunk benchmark is not initialized.";

    private SampleChunkBenchmarkState? state;

    /// <summary>Gets or sets the actual number of independently identified samples in each encoded batch.</summary>
    [Params(1, 32, 256)]
    public int RecordCount { get; set; } = DefaultControlRecordCount;

    /// <summary>Gets or sets the deterministic timestamp/value/tag distribution, not a database dataset size.</summary>
    [Params(SampleChunkBenchmarkCorpus.Regular, SampleChunkBenchmarkCorpus.Late, SampleChunkBenchmarkCorpus.Random)]
    public string Corpus { get; set; } = SampleChunkBenchmarkCorpus.Regular;

    /// <summary>Prepares both encodings and verifies every decoded field outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        const string SetupFailureMessage = "The sample chunk benchmark is already initialized.";

        if (state is not null)
        {
            throw new InvalidOperationException(SetupFailureMessage);
        }
        var runtime = EmbeddedBenchmarkRuntimeRegistration.Read();
        var candidate = new SampleChunkBenchmarkState(SampleChunkBenchmarkCorpus.Create(RecordCount, Corpus),
            runtime.Database, runtime.TimeSeriesExecution);
        SampleChunkBenchmarkManifest.Write(RecordCount, Corpus, candidate, BenchmarkArtifactRegistration.ReadSampleChunk());
        state = candidate;
    }

    /// <summary>Encodes one current native SampleRecord value per actual sample.</summary>
    /// <returns>The complete independent native value envelopes, excluding storage keys and journals.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory(EncodeCategory)]
    public byte[][] NativeRecordsEncode() => RequireState().NativeEncode();

    /// <summary>Encodes the same samples through the bounded checksummed generated chunk envelope.</summary>
    /// <returns>The complete native chunk value envelope.</returns>
    [Benchmark]
    [BenchmarkCategory(EncodeCategory)]
    public byte[] ChunkEncode() => RequireState().ChunkEncode();

    /// <summary>Decodes the current independent native value envelopes under one operation budget.</summary>
    /// <returns>The owned decoded sample records.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory(DecodeCategory)]
    public SampleRecord[] NativeRecordsDecode() => RequireState().NativeDecode();

    /// <summary>Decodes the same samples from the bounded lossless chunk after charging its actual bytes.</summary>
    /// <returns>The owned decoded sample records.</returns>
    [Benchmark]
    [BenchmarkCategory(DecodeCategory)]
    public SampleRecord[] ChunkDecode() => RequireState().ChunkDecode();

    /// <summary>Releases the corpus after a measurement process; repeated cleanup is harmless.</summary>
    [GlobalCleanup]
    public void Cleanup() => state = null;

    private SampleChunkBenchmarkState RequireState()
        => state ?? throw new InvalidOperationException(RequireStateFailureMessage);
}
