using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Measures full native and historical typed JSON serialization of document results.</summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 2, warmupCount: 3, iterationCount: 6, id: nameof(NativeSerialization))]
[IterationTime(200)]
public class NativeDocumentSerializationBenchmarks
{
    private const string RequireStateFailureMessage = "The native serialization fixture is not initialized.";

    private NativeSerializationBenchmarkState<DocumentResult>? state;

    /// <summary>Gets or sets the exact UTF-8 byte length of the embedded document JSON; envelope overhead is additional.</summary>
    [Params(NativeSerializationBenchmarkCorpus.SmallPayloadBytes, NativeSerializationBenchmarkCorpus.LargePayloadBytes)]
    public int PayloadBytes { get; set; } = NativeSerializationBenchmarkCorpus.SmallPayloadBytes;

    /// <summary>Prepares both formats and verifies identical typed content outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        const string SetupFailureMessage = "The native serialization fixture is already initialized.";

        if (state is not null)
        {
            throw new InvalidOperationException(SetupFailureMessage);
        }
        var candidate = new NativeSerializationBenchmarkState<DocumentResult>(NativeSerializationBenchmarkCorpus.Document(PayloadBytes));
        NativeSerializationBenchmarkManifest.Write(nameof(NativeDocumentSerializationBenchmarks), PayloadBytes, candidate, BenchmarkArtifactRegistration.ReadNativeSerialization());
        state = candidate;
    }

    /// <summary>Encodes the authentic typed corpus through the complete production native path.</summary>
    /// <returns>The generated native envelope bytes.</returns>
    [Benchmark]
    public byte[] NativeEncode() => RequireState().NativeEncode();

    /// <summary>Decodes the native envelope through bounded preflight and typed graph validation.</summary>
    /// <returns>The independently decoded typed corpus.</returns>
    [Benchmark]
    public DocumentResult NativeDecode() => RequireState().NativeDecode();

    /// <summary>Encodes the same typed corpus using the historical JsonDefaults UTF-8 codec.</summary>
    /// <returns>The typed JSON bytes.</returns>
    [Benchmark]
    public byte[] JsonEncode() => RequireState().JsonEncode();

    /// <summary>Decodes the historical typed JSON corpus using its actual JsonDefaults options.</summary>
    /// <returns>The independently decoded typed corpus.</returns>
    [Benchmark]
    public DocumentResult JsonDecode() => RequireState().JsonDecode();

    /// <summary>Releases the prepared corpus after measurement; repeated cleanup is harmless.</summary>
    [GlobalCleanup]
    public void Cleanup() => state = null;

    private NativeSerializationBenchmarkState<DocumentResult> RequireState()
        => state ?? throw new InvalidOperationException(RequireStateFailureMessage);
}
