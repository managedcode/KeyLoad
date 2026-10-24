using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

/// <summary>Measures full execution and historical typed JSON serialization of document results.</summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 2, warmupCount: 3, iterationCount: 6, id: nameof(OrleansSerialization))]
[IterationTime(200)]
public class DocumentSerializationBenchmarks
{
    private const string RequireStateFailureMessage = "The execution serialization fixture is not initialized.";

    private SerializationBenchmarkState<DocumentResult>? state;

    /// <summary>Gets or sets the exact UTF-8 byte length of the embedded document JSON; envelope overhead is additional.</summary>
    [Params(SerializationBenchmarkCorpus.SmallPayloadBytes, SerializationBenchmarkCorpus.LargePayloadBytes)]
    public int PayloadBytes { get; set; } = SerializationBenchmarkCorpus.SmallPayloadBytes;

    /// <summary>Prepares both formats and verifies identical typed content outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        const string SetupFailureMessage = "The execution serialization fixture is already initialized.";

        if (state is not null)
        {
            throw new InvalidOperationException(SetupFailureMessage);
        }
        var candidate = new SerializationBenchmarkState<DocumentResult>(SerializationBenchmarkCorpus.Document(PayloadBytes));
        SerializationBenchmarkManifest.Write(nameof(DocumentSerializationBenchmarks), PayloadBytes, candidate, BenchmarkArtifactRegistration.RuntimeReadSerialization());
        state = candidate;
    }

    /// <summary>Encodes the authentic typed corpus through the complete production execution path.</summary>
    /// <returns>The generated execution envelope bytes.</returns>
    [Benchmark]
    public byte[] RuntimeEncode() => RequireState().RuntimeEncode();

    /// <summary>Decodes the execution envelope through bounded preflight and typed graph validation.</summary>
    /// <returns>The independently decoded typed corpus.</returns>
    [Benchmark]
    public DocumentResult RuntimeDecode() => RequireState().RuntimeDecode();

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

    private SerializationBenchmarkState<DocumentResult> RequireState()
        => state ?? throw new InvalidOperationException(RequireStateFailureMessage);
}
