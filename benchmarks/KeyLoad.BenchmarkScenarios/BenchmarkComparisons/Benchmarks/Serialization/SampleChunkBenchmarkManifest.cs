using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class SampleChunkBenchmarkManifest
{
    private const int DescribeSingleItemCount = 1;
    private const string DescribeResultText = "development_codec_microbenchmark_control_only";
    private const string DescribeDescribeResultText = "SHA256_seed_sample_lane_Int32LE";

    internal const string DirectoryEnvironment = "KEYLOAD_CHUNK_BENCHMARK_MANIFEST_DIRECTORY";
    internal const string SourceHeadEnvironment = "KEYLOAD_CHUNK_SOURCE_HEAD";
    internal const string SourceInventoryEnvironment = "KEYLOAD_CHUNK_SOURCE_INVENTORY_SHA256";

    private const string CorrectnessContract = "every_field_original_offset_and_IEEE_bits_verified_before_timing";
    private const string AccountingContract = "complete_native_value_envelopes_excluding_keys_WAL_replication_indexes_and_storage_overhead";

    internal static void Write(int records, string corpus, SampleChunkBenchmarkState state,
        IOptions<BenchmarkArtifactOptions> artifactOptions)
    {
        const string WritePath2Text = "-";
        const string JsonFileExtension = ".json";
        const string WriteFailureMessage = "The sample chunk corpus manifest already belongs to a different source or corpus.";

        var settings = artifactOptions.Value;
        var directory = settings.SampleChunkDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        _ = settings.IsValid();
        var output = Describe(records, corpus, state, settings.SourceHead!, settings.SourceInventorySha256!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(output, JsonDefaults.Options);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, records.ToString(System.Globalization.CultureInfo.InvariantCulture) + WritePath2Text + corpus + JsonFileExtension);
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidOperationException(WriteFailureMessage);
            }
            return;
        }
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }

    private static object Describe(int records, string corpus, SampleChunkBenchmarkState state,
        string head, string sourceInventory)
        => new
        {
            schemaVersion = DescribeSingleItemCount,
            scope = DescribeResultText,
            fixture = nameof(SampleChunkSerializationBenchmarks),
            sourceHead = head,
            sourceInventorySha256 = sourceInventory,
            corpus,
            seed = SampleChunkBenchmarkCorpus.Seed,
            corpusGenerator = DescribeDescribeResultText,
            actualRecordCount = records,
            nativeValueBytes = state.NativeBytes,
            chunkValueBytes = state.ChunkBytes,
            nativeValueBytesPerSample = (double)state.NativeBytes / state.RecordCount,
            chunkValueBytesPerSample = (double)state.ChunkBytes / state.RecordCount,
            orderedNativeValuesSha256 = HashNative(state.NativeValues),
            chunkValueSha256 = Convert.ToHexStringLower(SHA256.HashData(state.ChunkValue)),
            correctness = CorrectnessContract,
            accounting = AccountingContract,
            machineName = Environment.MachineName,
            processorCount = Environment.ProcessorCount,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            operatingSystem = RuntimeInformation.OSDescription,
            runtime = RuntimeInformation.FrameworkDescription,
            databaseScaleEvidence = false,
            githubQualified = false,
            canonicalChunkStorage = false,
            rewriteCostQualified = false,
            correctionRecoveryQualified = false
        };

    private static string HashNative(IReadOnlyList<byte[]> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
            hash.AppendData(length);
            hash.AppendData(value);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

}
