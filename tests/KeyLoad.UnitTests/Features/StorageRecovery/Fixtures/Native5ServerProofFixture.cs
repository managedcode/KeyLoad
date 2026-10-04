using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record Native5ServerParserCase(string Name, string Receipt, string Manifest,
    string ExpectedProducer, string ExpectedReference, bool OversizedManifest = false, int LayerCountOverride = 0);

internal static class Native5ServerProofFixture
{
    internal const string PriorRevision = "7784b6b46b98ce994dd98070dc1f58fe4e506b91";
    internal const string PriorTree = "b03bf1301a03b3fe00f419c3c7bf5285a34b63f9";
    internal const string InventorySha = "2d60112c44b55fdbcfd36bdeb14bf821e38d55e43df09c0c85f10558a5e332d0";
    internal const string ArchiveSha = "3ebe633c065bf623a9e1a13eca3eabbc5135635e7ddd2cbe127911bd527b12fb";
    internal const string CurrentProducerSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string ExpectedPriorJob = "docker-rf3";
    internal const int PriorFileCount = 2557;
    internal const long PriorExpandedBytes = 22520638;
    internal const long PriorArchiveBytes = 24606720;
    internal const long MaximumArchiveBytes = 4L * 1024 * 1024 * 1024;

    internal const string SchemaVersion = "schemaVersion";
    internal const string Kind = "kind";
    internal const string Producer = "producer";
    internal const string ImageSource = "imageSource";
    internal const string Bases = "bases";
    internal const string Image = "image";
    internal const string SourceSha = "sourceSha";
    internal const string RunId = "runId";
    internal const string RunAttempt = "runAttempt";
    internal const string Repository = "repository";
    internal const string Ref = "ref";
    internal const string Workflow = "workflow";
    internal const string Job = "job";
    internal const string Revision = "revision";
    internal const string TreeSha = "treeSha";
    internal const string ArchiveFile = "archiveFile";
    internal const string ArchiveSha256 = "archiveSha256";
    internal const string ArchiveBytes = "archiveBytes";
    internal const string InventoryFile = "inventoryFile";
    internal const string InventoryFileSha256 = "inventoryFileSha256";
    internal const string SourceInventorySha256 = "sourceInventorySha256";
    internal const string FileCount = "fileCount";
    internal const string ExpandedBytes = "expandedBytes";
    internal const string OverlayCount = "overlayCount";
    internal const string Sdk = "sdk";
    internal const string Runtime = "runtime";
    internal const string Registry = "registry";
    internal const string Name = "name";
    internal const string TaggedReference = "taggedReference";
    internal const string Reference = "reference";
    internal const string ManifestFile = "manifestFile";
    internal const string ManifestSha256 = "manifestSha256";
    internal const string RegistryDigest = "registryDigest";
    internal const string ContentType = "contentType";
    internal const string ConfigId = "configId";
    internal const string RevisionLabel = "revisionLabel";
    internal const string MediaType = "mediaType";
    internal const string Config = "config";
    internal const string Digest = "digest";
    internal const string Size = "size";
    internal const string Layers = "layers";
    internal const string Urls = "urls";
    internal const string Data = "data";
    internal const string Subject = "subject";
    internal const string ArtifactType = "artifactType";
    internal const string Manifests = "manifests";
    internal const string Annotations = "annotations";
    internal const string TitleAnnotationKey = "org.opencontainers.image.title";
    internal const string TitleAnnotationValue = "keyload-server";
    private const string ReceiptKind = "keyload.prior-server-image-proof.v1";
    internal const string ManifestContentType = "application/vnd.oci.image.manifest.v1+json";
    private const string ConfigContentType = "application/vnd.oci.image.config.v1+json";
    private const string LayerContentType = "application/vnd.oci.image.layer.v1.tar+gzip";
    private const string SdkBase = "mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317";
    private const string RuntimeBase = "mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4";
    private const string RegistryBase = "registry:3.1.2@sha256:ddf754342cfc8acc51a56d5d0ab6af06826461864460636d8bd5c546dab2a7b8";
    private const string ServerPrefix = "127.0.0.1:5000/keyload/server";
    private const string ImageDigestPrefix = "sha256:";
    private const string TaggedSeparator = ":";
    private const string RunSeparator = "-";
    internal const string DigestSeparator = "@";
    private const string RepositoryValue = "managedcode/KeyLoad";
    private const string RefValue = "refs/heads/main";
    private const string WorkflowValue = "CI";
    private const string RunIdValue = "12345678901234";
    private const string AttemptValue = "1";
    internal const string ImageConfigDigest = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    internal const string LayerDigest = "sha256:2222222222222222222222222222222222222222222222222222222222222222";
    internal const string DockerManifestContentType = "application/vnd.docker.distribution.manifest.v2+json";
    internal const string DockerConfigContentType = "application/vnd.docker.container.image.v1+json";
    internal const string DockerLayerContentType = "application/vnd.docker.image.rootfs.diff.tar.gzip";
    internal const string OciLayerContentType = "application/vnd.oci.image.layer.v1.tar";
    internal const string OciZstdLayerContentType = "application/vnd.oci.image.layer.v1.tar+zstd";
    private const string InventoryFileValue = "prior-server-source-inventory.json";
    private const string ArchiveFileValue = "prior-server-source.tar";
    private const string ManifestFileValue = "prior-server-manifest.json";

    internal static Native5ServerParserCase Valid()
    {
        var manifest = CreateManifest().ToJsonString();
        var manifestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
        var registryDigest = ImageDigestPrefix + manifestHash;
        var producer = CreateProducer();
        var tag = PriorRevision + RunSeparator + RunIdValue + RunSeparator + AttemptValue;
        var taggedReference = ServerPrefix + TaggedSeparator + tag;
        var finalReference = taggedReference + DigestSeparator + registryDigest;
        var receipt = CreateReceipt(producer, taggedReference, finalReference, registryDigest);
        return new("valid synthetic parser input", receipt.ToJsonString(), manifest, producer.ToJsonString(), finalReference);
    }

    private static JsonObject CreateManifest()
        => new()
        {
            [SchemaVersion] = 2,
            [MediaType] = ManifestContentType,
            [Config] = new JsonObject { [MediaType] = ConfigContentType, [Digest] = ImageConfigDigest, [Size] = 1 },
            [Layers] = new JsonArray { new JsonObject { [MediaType] = LayerContentType, [Digest] = LayerDigest, [Size] = 1 } },
            [Annotations] = new JsonObject { [TitleAnnotationKey] = TitleAnnotationValue }
        };

    private static JsonObject CreateProducer()
        => new()
        {
            [SourceSha] = CurrentProducerSha,
            [RunId] = RunIdValue,
            [RunAttempt] = AttemptValue,
            [Repository] = RepositoryValue,
            [Ref] = RefValue,
            [Workflow] = WorkflowValue,
            [Job] = ExpectedPriorJob
        };

    private static JsonObject CreateReceipt(JsonObject producer, string taggedReference, string finalReference,
        string registryDigest)
        => new()
        {
            [SchemaVersion] = 1,
            [Kind] = ReceiptKind,
            [Producer] = producer.DeepClone(),
            [ImageSource] = CreateImageSource(),
            [Bases] = CreateBases(),
            [Image] = CreateImage(taggedReference, finalReference, registryDigest)
        };

    private static JsonObject CreateImageSource()
        => new()
        {
            [Revision] = PriorRevision,
            [TreeSha] = PriorTree,
            [ArchiveFile] = ArchiveFileValue,
            [ArchiveSha256] = ArchiveSha,
            [ArchiveBytes] = PriorArchiveBytes,
            [InventoryFile] = InventoryFileValue,
            [InventoryFileSha256] = new string('b', 64),
            [SourceInventorySha256] = InventorySha,
            [FileCount] = PriorFileCount,
            [ExpandedBytes] = PriorExpandedBytes,
            [OverlayCount] = 0
        };

    private static JsonObject CreateBases()
        => new() { [Sdk] = SdkBase, [Runtime] = RuntimeBase, [Registry] = RegistryBase };

    private static JsonObject CreateImage(string taggedReference, string finalReference, string registryDigest)
        => new()
        {
            [Name] = "server",
            [TaggedReference] = taggedReference,
            [Reference] = finalReference,
            [ManifestFile] = ManifestFileValue,
            [ManifestSha256] = registryDigest,
            [RegistryDigest] = registryDigest,
            [ContentType] = ManifestContentType,
            [ConfigId] = ImageConfigDigest,
            [RevisionLabel] = PriorRevision
        };

}
