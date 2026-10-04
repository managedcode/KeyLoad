using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed record ComparisonImageReceipt(string ServerImage, string RunnerImage)
{
    /// <summary>AC-IMAGE-002 verifies actual job-registry manifest bytes and official source context.</summary>
    internal static async Task<ComparisonImageReceipt> ReadAsync(CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.ReceiptEnvironment));
        using var document = JsonDocument.Parse(await ReadBoundedAsync(path,
            ComparisonImageProtocol.MaximumReceiptBytes, cancellationToken));
        var receipt = document.RootElement;
        await Assert.That(receipt.GetProperty(ComparisonImageProtocol.Schema).GetInt32())
            .IsEqualTo(ComparisonImageProtocol.SchemaVersion);
        var source = ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.ShaEnvironment);
        await Assert.That(receipt.GetProperty(ComparisonImageProtocol.Source).GetString()).IsEqualTo(source);
        await VerifyGitHubAsync(receipt.GetProperty(ComparisonImageProtocol.GitHub));
        var directory = Path.GetDirectoryName(path)!;
        var images = receipt.GetProperty(ComparisonImageProtocol.Images);
        var server = await VerifyImageAsync(images.GetProperty(ComparisonImageProtocol.Server), directory,
            ComparisonImageProtocol.ServerManifest, source, cancellationToken);
        var runner = await VerifyImageAsync(images.GetProperty(ComparisonImageProtocol.Runner), directory,
            ComparisonImageProtocol.RunnerManifest, source, cancellationToken);
        return new(server, runner);
    }

    private static async Task VerifyGitHubAsync(JsonElement github)
    {
        var runId = long.Parse(ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.RunEnvironment),
            CultureInfo.InvariantCulture);
        var attempt = int.Parse(ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.AttemptEnvironment),
            CultureInfo.InvariantCulture);
        await Assert.That(long.Parse(github.GetProperty(ComparisonImageProtocol.RunId).GetString()!,
            CultureInfo.InvariantCulture)).IsEqualTo(runId);
        await Assert.That(int.Parse(github.GetProperty(ComparisonImageProtocol.Attempt).GetString()!,
            CultureInfo.InvariantCulture)).IsEqualTo(attempt);
        await Assert.That(github.GetProperty(ComparisonImageProtocol.Repository).GetString())
            .IsEqualTo(ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.RepositoryEnvironment));
        await Assert.That(github.GetProperty(ComparisonImageProtocol.Ref).GetString())
            .IsEqualTo(ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.RefEnvironment));
    }

    private static async Task<string> VerifyImageAsync(JsonElement image, string directory, string manifestName,
        string source, CancellationToken cancellationToken)
    {
        await Assert.That(image.GetProperty(ComparisonImageProtocol.Manifest).GetString()).IsEqualTo(manifestName);
        var bytes = await ReadBoundedAsync(Path.Combine(directory, manifestName),
            ComparisonImageProtocol.MaximumManifestBytes, cancellationToken);
        var digest = ComparisonImageProtocol.ShaPrefix + Convert.ToHexStringLower(SHA256.HashData(bytes));
        await Assert.That(image.GetProperty(ComparisonImageProtocol.Digest).GetString()).IsEqualTo(digest);
        await Assert.That(image.GetProperty(ComparisonImageProtocol.RegistryDigest).GetString()).IsEqualTo(digest);
        await Assert.That(image.GetProperty(ComparisonImageProtocol.Revision).GetString()).IsEqualTo(source);
        using var manifest = JsonDocument.Parse(bytes);
        var configId = manifest.RootElement.GetProperty(ComparisonImageProtocol.Config)
            .GetProperty(ComparisonImageProtocol.ConfigDigest).GetString();
        await Assert.That(image.GetProperty(ComparisonImageProtocol.ConfigId).GetString()).IsEqualTo(configId);
        var reference = image.GetProperty(ComparisonImageProtocol.Reference).GetString()!;
        await Assert.That(reference.EndsWith(ComparisonImageProtocol.ImageDigestPrefix + digest[ComparisonImageProtocol.ShaPrefix.Length..],
            StringComparison.Ordinal)).IsTrue();
        return reference;
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > maximumBytes)
        {
            throw new InvalidOperationException(ComparisonImageProtocol.InvalidReceipt);
        }
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length > maximumBytes)
        {
            throw new InvalidOperationException(ComparisonImageProtocol.InvalidReceipt);
        }
        return bytes;
    }
}
