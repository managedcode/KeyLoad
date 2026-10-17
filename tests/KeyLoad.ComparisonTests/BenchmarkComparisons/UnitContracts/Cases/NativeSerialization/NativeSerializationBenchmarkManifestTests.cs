using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

[NotInParallel]
internal sealed class NativeSerializationBenchmarkManifestTests
{
    [Test]
    public async Task AcIsPerf001SetupRetainsActualDeterministicCorpusHashesAndRejectsDrift()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-native-corpus-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable(NativeSerializationBenchmarkManifest.DirectoryVariable);
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        try
        {
            Environment.SetEnvironmentVariable(NativeSerializationBenchmarkManifest.DirectoryVariable, directory);
            var state = new NativeSerializationBenchmarkState<DocumentResult>(NativeSerializationBenchmarkCorpus.Document(1024));
            var name = nameof(NativeDocumentSerializationBenchmarks);
            NativeSerializationBenchmarkManifest.Write(name, 1024, state, Microsoft.Extensions.Options.Options.Create(new BenchmarkArtifactOptions { NativeSerializationDirectory = directory }));
            var path = Path.Combine(directory, name + "-1024.json");
            var first = await File.ReadAllBytesAsync(path, cancellationToken);
            NativeSerializationBenchmarkManifest.Write(name, 1024, state, Microsoft.Extensions.Options.Options.Create(new BenchmarkArtifactOptions { NativeSerializationDirectory = directory }));
            var second = await File.ReadAllBytesAsync(path, cancellationToken);
            await Assert.That(second.AsSpan().SequenceEqual(first)).IsTrue();
            using var receipt = JsonDocument.Parse(first);
            await Assert.That(receipt.RootElement.GetProperty(NativeSerializationReportFields.CorpusSha256).GetString())
                .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(state.JsonBytes)));
            await Assert.That(receipt.RootElement.GetProperty(NativeSerializationReportFields.NativeSha256).GetString())
                .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(state.NativeBytes)));
            await Assert.That(receipt.RootElement.GetProperty(NativeSerializationReportFields.NativeBytes).GetInt32()).IsEqualTo(state.NativeBytes.Length);
            await Assert.That(receipt.RootElement.GetProperty(NativeSerializationReportFields.JsonBytes).GetInt32()).IsEqualTo(state.JsonBytes.Length);
            state.NativeBytes[0] ^= 1;
            await Assert.That(() => NativeSerializationBenchmarkManifest.Write(name, 1024, state, Microsoft.Extensions.Options.Options.Create(new BenchmarkArtifactOptions { NativeSerializationDirectory = directory }))).Throws<InvalidOperationException>();
            await Assert.That(Directory.GetFiles(directory, "*.tmp").Length).IsEqualTo(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(NativeSerializationBenchmarkManifest.DirectoryVariable, previous);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
