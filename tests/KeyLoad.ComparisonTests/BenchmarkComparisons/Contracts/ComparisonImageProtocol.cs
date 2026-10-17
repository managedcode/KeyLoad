namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ComparisonImageProtocol
{
    internal const string ReceiptEnvironment = "KEYLOAD_IMAGE_RECEIPT";
    internal const string ShaEnvironment = "GITHUB_SHA";
    internal const string RunEnvironment = "GITHUB_RUN_ID";
    internal const string AttemptEnvironment = "GITHUB_RUN_ATTEMPT";
    internal const string RepositoryEnvironment = "GITHUB_REPOSITORY";
    internal const string RefEnvironment = "GITHUB_REF";
    internal const string Schema = "schemaVersion";
    internal const string Source = "sourceRevision";
    internal const string GitHub = "github";
    internal const string RunId = "runId";
    internal const string Attempt = "attempt";
    internal const string Repository = "repository";
    internal const string Ref = "ref";
    internal const string Images = "images";
    internal const string Server = "server";
    internal const string Runner = "comparisons";
    internal const string Reference = "reference";
    internal const string Digest = "manifestDigest";
    internal const string RegistryDigest = "registryDigest";
    internal const string Manifest = "manifestFile";
    internal const string Revision = "revisionLabel";
    internal const string ConfigId = "configId";
    internal const string Config = "config";
    internal const string ConfigDigest = "digest";
    internal const string ServerManifest = "server-manifest.json";
    internal const string RunnerManifest = "comparisons-manifest.json";
    internal const string ShaPrefix = "sha256:";
    internal const string ImageDigestPrefix = "@sha256:";
    internal const char TagSeparator = ':';
    internal const string Node1 = "node1";
    internal const string Node2 = "node2";
    internal const string Node3 = "node3";
    internal const string MissingInput = "The actual GitHub image qualification input is required.";
    internal const string InvalidReceipt = "The actual image qualification receipt is invalid or oversized.";
    internal const int SchemaVersion = 1;
    internal const int NodeCount = 3;
    internal const int MaximumReceiptBytes = 65_536;
    internal const int MaximumManifestBytes = 1_048_576;

    internal static string RequiredEnvironment(string key) => Environment.GetEnvironmentVariable(key)
        ?? throw new InvalidOperationException(MissingInput);
}
