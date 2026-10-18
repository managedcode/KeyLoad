using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ImageToolingEvidenceContractTests
{
    private const string MetadataOperation = "metadata";
    private const string ManifestOperation = "manifest";
    private const string DiagnosticsOperation = "diagnostics";
    private const string SourceSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherSourceSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string SecretSentinel = "private-runner-token-do-not-print";
    private const string ConfigId = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string OtherConfigId = "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string ImageName = "server";
    private const string TaggedReference = "127.0.0.1:5000/keyload/server:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-37070000000-2";
    private const string OciManifestContentType = "application/vnd.oci.image.manifest.v1+json";
    private const string OciConfigContentType = "application/vnd.oci.image.config.v1+json";
    private const string ManifestBytesProperty = "manifestBytesBase64";
    private const string ManifestDigestProperty = "manifestDigest";
    private const string RegistryDigestProperty = "registryDigest";
    private const string ConfigIdProperty = "configId";
    private const string MetadataConfigIdProperty = "configImageId";
    private const string SourceRevisionProperty = "sourceRevision";
    private const string RevisionLabelProperty = "revisionLabel";
    private const string FinalReferenceProperty = "finalReference";
    private const string RejectedMetadataMessage = "Docker returned invalid bounded image metadata.";
    private const string RejectedManifestMessage = "The local registry returned invalid image manifest evidence.";
    private const int ManifestVersion = 2;
    private const int DescriptorSize = 123;

    [Test]
    public async Task AC_IMAGE_002_MetadataRequiresAValidConfigIdAndExactSourceRevision()
    {
        var valid = await RunAsync(new
        {
            operation = MetadataOperation,
            output = $"{ConfigId}|{SourceSha}",
            expectedRevision = SourceSha,
        });
        await Assert.That(valid.Succeeded).IsTrue();
        await Assert.That(valid.Value.GetProperty(MetadataConfigIdProperty).GetString()).IsEqualTo(ConfigId);
        await Assert.That(valid.Value.GetProperty(SourceRevisionProperty).GetString()).IsEqualTo(SourceSha);

        var wrongRevision = await RunAsync(new
        {
            operation = MetadataOperation,
            output = $"{ConfigId}|{OtherSourceSha}",
            expectedRevision = SourceSha,
        });
        await Assert.That(wrongRevision.Succeeded).IsFalse();
        await Assert.That(wrongRevision.Message).IsEqualTo(RejectedMetadataMessage);

        var malformedConfig = await RunAsync(new
        {
            operation = MetadataOperation,
            output = $"sha256:bad|{SourceSha}",
            expectedRevision = SourceSha,
        });
        await Assert.That(malformedConfig.Succeeded).IsFalse();
    }

    [Test]
    public async Task AC_IMAGE_002_ManifestBytesMustMatchHeaderConfigAndImmutableReference()
    {
        var bytes = ManifestBytes(ConfigId);
        var digest = Digest(bytes);
        var accepted = await RunAsync(ManifestRequest(bytes, digest, ConfigId));
        await Assert.That(accepted.Succeeded).IsTrue();
        await Assert.That(accepted.Value.GetProperty(ManifestDigestProperty).GetString()).IsEqualTo(digest);
        await Assert.That(accepted.Value.GetProperty(RegistryDigestProperty).GetString()).IsEqualTo(digest);
        await Assert.That(accepted.Value.GetProperty(ConfigIdProperty).GetString()).IsEqualTo(ConfigId);
        await Assert.That(accepted.Value.GetProperty(RevisionLabelProperty).GetString()).IsEqualTo(SourceSha);
        await Assert.That(accepted.Value.GetProperty(ManifestBytesProperty).GetString()).IsEqualTo(Convert.ToBase64String(bytes));
        await Assert.That(accepted.Value.GetProperty(FinalReferenceProperty).GetString()).IsEqualTo($"{TaggedReference}@{digest}");

        var wrongHeader = await RunAsync(ManifestRequest(bytes, Digest(Encoding.UTF8.GetBytes(OtherSourceSha)), ConfigId));
        await Assert.That(wrongHeader.Succeeded).IsFalse();
        await Assert.That(wrongHeader.Message).IsEqualTo(RejectedManifestMessage);

        var wrongConfig = await RunAsync(ManifestRequest(bytes, digest, OtherConfigId));
        await Assert.That(wrongConfig.Succeeded).IsFalse();

        var configConfusedWithManifestDigest = await RunAsync(ManifestRequest(bytes, digest, digest));
        await Assert.That(configConfusedWithManifestDigest.Succeeded).IsFalse();

        var wrongContentType = await RunAsync(ManifestRequest(bytes, digest, ConfigId, "text/plain"));
        await Assert.That(wrongContentType.Succeeded).IsFalse();

        var malformedBytes = Encoding.UTF8.GetBytes("not-an-oci-manifest");
        var malformed = await RunAsync(ManifestRequest(malformedBytes, Digest(malformedBytes), ConfigId));
        await Assert.That(malformed.Succeeded).IsFalse();
    }

    [Test]
    public async Task AC_IMAGE_006_BoundedDiagnosticsRedactCredentialShapes()
    {
        const string bearer = "ghp_superlongtesttokenvalue123456789";
        const string uriCredential = "https://runner-user:private-uri-secret@registry.invalid/v2";
        var result = await RunAsync(new
        {
            operation = DiagnosticsOperation,
            output = $"Authorization: Bearer {bearer}\npassword={SecretSentinel}\n{uriCredential}",
        });

        await Assert.That(result.Succeeded).IsTrue();
        var sanitized = result.Value.GetString()!;
        await Assert.That(sanitized.Contains(bearer, StringComparison.Ordinal)).IsFalse();
        await Assert.That(sanitized.Contains(SecretSentinel, StringComparison.Ordinal)).IsFalse();
        await Assert.That(sanitized.Contains("runner-user:private-uri-secret", StringComparison.Ordinal)).IsFalse();
        await Assert.That(sanitized.Contains("[REDACTED]", StringComparison.Ordinal)).IsTrue();
    }

    private static object ManifestRequest(byte[] bytes, string digest, string configId,
        string contentType = OciManifestContentType) => new
        {
            operation = ManifestOperation,
            bytesBase64 = Convert.ToBase64String(bytes),
            digestHeader = digest,
            contentTypeHeader = contentType,
            expectedRevision = SourceSha,
            configId,
            imageName = ImageName,
            taggedReference = TaggedReference,
        };

    private static byte[] ManifestBytes(string configId)
        => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = ManifestVersion,
            mediaType = OciManifestContentType,
            config = new
            {
                mediaType = OciConfigContentType,
                size = DescriptorSize,
                digest = configId
            },
            layers = Array.Empty<object>(),
        });

    private static string Digest(byte[] bytes)
        => $"sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))}";

    private static Task<ImageToolingContractNodeResult> RunAsync(object request)
        => ImageToolingContractNodeProcess.RunAsync(request, TestContext.Current!.Execution.CancellationToken);
}
