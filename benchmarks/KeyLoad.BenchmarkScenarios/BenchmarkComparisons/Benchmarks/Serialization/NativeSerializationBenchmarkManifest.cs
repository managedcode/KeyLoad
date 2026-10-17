using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class NativeSerializationBenchmarkManifest
{
    internal const string DirectoryVariable = "KEYLOAD_NATIVE_SERIALIZATION_CORPUS_DIRECTORY";
    private const string TemporaryIdentityFormat = "N";

    internal static void Write<T>(string fixture, int payloadBytes, NativeSerializationBenchmarkState<T> state,
        IOptions<BenchmarkArtifactOptions> artifactOptions)
    {
        const string WritePath2Text = "-";
        const string JsonFileExtension = ".json";

        var directory = artifactOptions.Value.NativeSerializationDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        var jsonHash = Convert.ToHexStringLower(SHA256.HashData(state.JsonBytes));
        var receipt = JsonSerializer.SerializeToUtf8Bytes(new
        {
            fixture,
            payloadBytes,
            corpusSha256 = jsonHash,
            nativeSha256 = Convert.ToHexStringLower(SHA256.HashData(state.NativeBytes)),
            nativeBytes = state.NativeBytes.Length,
            jsonSha256 = jsonHash,
            jsonBytes = state.JsonBytes.Length
        });
        var path = Path.Combine(directory, fixture + WritePath2Text + payloadBytes.ToString(CultureInfo.InvariantCulture) + JsonFileExtension);
        WriteConsistent(path, receipt);
    }

    private static void WriteConsistent(string path, byte[] receipt)
    {
        const string WriteConsistentComparisonText = ".";
        const string TemporaryFileExtension = ".tmp";
        const string WriteConsistentFailureMessage = "Repeated native benchmark setup changed its corpus manifest.";

        var temporary = path + WriteConsistentComparisonText + Guid.NewGuid().ToString(TemporaryIdentityFormat) + TemporaryFileExtension;
        try
        {
            File.WriteAllBytes(temporary, receipt);
            try
            {
                File.Move(temporary, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(receipt))
                {
                    throw new InvalidOperationException(WriteConsistentFailureMessage);
                }
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
