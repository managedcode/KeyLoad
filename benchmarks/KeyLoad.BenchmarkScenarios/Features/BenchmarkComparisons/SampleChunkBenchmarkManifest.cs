using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class SampleChunkBenchmarkManifest
{
    internal const string DirectoryEnvironment = "KEYLOAD_CHUNK_BENCHMARK_MANIFEST_DIRECTORY";
    internal const string SourceHeadEnvironment = "KEYLOAD_CHUNK_SOURCE_HEAD";
    internal const string SourceInventoryEnvironment = "KEYLOAD_CHUNK_SOURCE_INVENTORY_SHA256";
    private const string InvalidSourceMessage = "The sample chunk measurement source identity is missing or malformed.";

    internal static void Write(int records, string corpus, SampleChunkBenchmarkState state)
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryEnvironment);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        var head = RequireHex(SourceHeadEnvironment, 40);
        var sourceInventory = RequireHex(SourceInventoryEnvironment, 64);
        var output = Describe(records, corpus, state, head, sourceInventory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(output, JsonDefaults.Options);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, records.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + corpus + ".json");
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidOperationException("The sample chunk corpus manifest already belongs to a different source or corpus.");
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
            schemaVersion = 1,
            scope = "development_codec_microbenchmark_control_only",
            fixture = nameof(SampleChunkSerializationBenchmarks),
            sourceHead = head,
            sourceInventorySha256 = sourceInventory,
            corpus,
            seed = SampleChunkBenchmarkCorpus.Seed,
            corpusGenerator = "SHA256_seed_sample_lane_Int32LE",
            actualRecordCount = records,
            nativeValueBytes = state.NativeBytes,
            chunkValueBytes = state.ChunkBytes,
            nativeValueBytesPerSample = (double)state.NativeBytes / state.RecordCount,
            chunkValueBytesPerSample = (double)state.ChunkBytes / state.RecordCount,
            orderedNativeValuesSha256 = HashNative(state.NativeValues),
            chunkValueSha256 = Convert.ToHexStringLower(SHA256.HashData(state.ChunkValue)),
            correctness = "every_field_original_offset_and_IEEE_bits_verified_before_timing",
            accounting = "complete_native_value_envelopes_excluding_keys_WAL_replication_indexes_and_storage_overhead",
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

    private static string RequireHex(string environment, int length)
    {
        var value = Environment.GetEnvironmentVariable(environment);
        if (value is null || value.Length != length || value.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidOperationException(InvalidSourceMessage);
        }
        return value;
    }
}
