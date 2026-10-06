using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureImageIdentity
{
    private const string ReceiptEnvironment = "KEYLOAD_IMAGE_RECEIPT";
    private const string SourceEnvironment = "GITHUB_SHA";
    private const string MissingIdentity = "Actual GitHub RF3 image identity is required.";
    private const string ReceiptSchema = "schemaVersion";
    private const string SourceProperty = "sourceRevision";
    private const string ImagesProperty = "images";
    private const string ServerProperty = "server";
    private const string ManifestProperty = "manifestFile";
    private const string ManifestName = "server-manifest.json";
    private const string DigestProperty = "manifestDigest";
    private const string RegistryDigestProperty = "registryDigest";
    private const string RevisionProperty = "revisionLabel";
    private const string ReferenceProperty = "reference";
    private const string DigestPrefix = "sha256:";
    private const string ReferenceDigestPrefix = "@sha256:";
    private const char TagSeparator = ':';
    private const int SchemaVersion = 1;
    private const int MaximumReceiptBytes = 65_536;
    private const int MaximumManifestBytes = 1_048_576;

    /// <summary>AC-IMAGE-002/003 checks actual immutable manifest bytes and all three RF3 resources before start.</summary>
    internal static async Task VerifyAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        var reference = await ReadVerifiedReferenceAsync(cancellationToken);
        var digest = reference[(reference.LastIndexOf(ReferenceDigestPrefix, StringComparison.Ordinal)
            + ReferenceDigestPrefix.Length)..];
        await VerifyResourcesAsync(app, reference, digest);
    }

    internal static Task<ClusterFixtureSourceImage> ReadVerifiedImageAsync(CancellationToken cancellationToken)
        => ReadVerifiedReceiptAsync(cancellationToken);

    /// <summary>Verifies the original receipt and returns the identity from the same bytes.</summary>
    internal static async Task<string> ReadVerifiedReferenceAsync(CancellationToken cancellationToken)
    {
        var image = await ReadVerifiedReceiptAsync(cancellationToken).ConfigureAwait(false);
        return image.Reference;
    }

    private static async Task<ClusterFixtureSourceImage> ReadVerifiedReceiptAsync(CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(Environment.GetEnvironmentVariable(ReceiptEnvironment)
            ?? throw new InvalidOperationException(MissingIdentity));
        var source = Environment.GetEnvironmentVariable(SourceEnvironment)
            ?? throw new InvalidOperationException(MissingIdentity);
        var bytes = await ReadBoundedAsync(path, MaximumReceiptBytes, cancellationToken).ConfigureAwait(false);
        using var receipt = JsonDocument.Parse(bytes);
        await Assert.That(receipt.RootElement.GetProperty(ReceiptSchema).GetInt32()).IsEqualTo(SchemaVersion);
        await Assert.That(receipt.RootElement.GetProperty(SourceProperty).GetString()).IsEqualTo(source);
        var image = receipt.RootElement.GetProperty(ImagesProperty).GetProperty(ServerProperty);
        var digest = image.GetProperty(DigestProperty).GetString();
        await Assert.That(image.GetProperty(ManifestProperty).GetString()).IsEqualTo(ManifestName);
        if (string.IsNullOrWhiteSpace(digest))
        {
            throw new InvalidOperationException(MissingIdentity);
        }
        var manifestPath = Path.Combine(Path.GetDirectoryName(path)!, ManifestName);
        var manifestBytes = await ReadBoundedAsync(manifestPath, MaximumManifestBytes, cancellationToken)
            .ConfigureAwait(false);
        var expectedDigest = DigestPrefix + Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
        await Assert.That(digest).IsEqualTo(expectedDigest);
        await Assert.That(image.GetProperty(RegistryDigestProperty).GetString()).IsEqualTo(expectedDigest);
        await Assert.That(image.GetProperty(RevisionProperty).GetString()).IsEqualTo(source);
        var reference = image.GetProperty(ReferenceProperty).GetString()!;
        await Assert.That(reference.EndsWith(ReferenceDigestPrefix + expectedDigest[DigestPrefix.Length..],
            StringComparison.Ordinal)).IsTrue();
        return new(reference, digest, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static async Task VerifyResourcesAsync(DistributedApplication app, string reference, string digest)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var nodes = model.Resources.OfType<ContainerResource>().Where(node => ClusterFixtureProtocol.IsNodeName(node.Name)).ToArray();
        await Assert.That(nodes.Length).IsEqualTo(ClusterFixtureProtocol.NodeCount);
        var taggedImage = reference[..reference.LastIndexOf(ReferenceDigestPrefix, StringComparison.Ordinal)];
        var imageName = taggedImage[..taggedImage.LastIndexOf(TagSeparator)];
        foreach (var node in nodes)
        {
            var annotation = node.Annotations.OfType<ContainerImageAnnotation>().Single();
            await Assert.That(annotation.Image).IsEqualTo(imageName);
            await Assert.That(annotation.Tag).IsNull();
            await Assert.That(annotation.SHA256).IsEqualTo(digest);
            await Assert.That(node.TryGetContainerImageName(out var actual)).IsTrue();
            await Assert.That(actual!.EndsWith(ReferenceDigestPrefix + digest, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        await Assert.That(length is > 0 && length <= maximumBytes).IsTrue();
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        await Assert.That(bytes.Length <= maximumBytes).IsTrue();
        return bytes;
    }
}

internal sealed record ClusterFixtureSourceImage(string Reference, string ManifestDigest, string ReceiptSha256);
