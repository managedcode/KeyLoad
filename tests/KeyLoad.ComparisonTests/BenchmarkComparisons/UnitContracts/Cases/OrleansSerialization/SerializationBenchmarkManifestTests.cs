using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

[NotInParallel]
internal sealed class SerializationBenchmarkManifestTests
{
    [Test]
    public async Task AcIsPerf001SetupRetainsActualDeterministicCorpusHashesAndRejectsDrift()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-execution-corpus-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable(SerializationBenchmarkManifest.DirectoryVariable);
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        try
        {
            Environment.SetEnvironmentVariable(SerializationBenchmarkManifest.DirectoryVariable, directory);
            var state = new SerializationBenchmarkState<DocumentResult>(SerializationBenchmarkCorpus.Document(1024));
            var name = nameof(DocumentSerializationBenchmarks);
            SerializationBenchmarkManifest.Write(name, 1024, state, Microsoft.Extensions.Options.Options.Create(new BenchmarkArtifactOptions { SerializationDirectory = directory }));
            var path = Path.Combine(directory, name + "-1024.json");
            var first = await File.ReadAllBytesAsync(path, cancellationToken);
            SerializationBenchmarkManifest.Write(name, 1024, state, Microsoft.Extensions.Options.Options.Create(new BenchmarkArtifactOptions { SerializationDirectory = directory }));
            var second = await File.ReadAllBytesAsync(path, cancellationToken);
            await Assert.That(second.AsSpan().SequenceEqual(first)).IsTrue();
            using var receipt = JsonDocument.Parse(first);
            await Assert.That(receipt.RootElement.GetProperty(SerializationReportFields.CorpusSha256).GetString())
                .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(state.JsonBytes)));
            await Assert.That(receipt.RootElement.GetProperty(SerializationReportFields.RuntimeSha256).GetString())
                .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(state.RuntimeBytes)));
            await Assert.That(receipt.RootElement.GetProperty(SerializationReportFields.RuntimeBytes).GetInt32()).IsEqualTo(state.RuntimeBytes.Length);
            await Assert.That(receipt.RootElement.GetProperty(SerializationReportFields.JsonBytes).GetInt32()).IsEqualTo(state.JsonBytes.Length);
            state.RuntimeBytes[0] ^= 1;
            await Assert.That(() => SerializationBenchmarkManifest.Write(name, 1024, state, Microsoft.Extensions.Options.Options.Create(new BenchmarkArtifactOptions { SerializationDirectory = directory }))).Throws<InvalidOperationException>();
            await Assert.That(Directory.GetFiles(directory, "*.tmp").Length).IsEqualTo(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SerializationBenchmarkManifest.DirectoryVariable, previous);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
