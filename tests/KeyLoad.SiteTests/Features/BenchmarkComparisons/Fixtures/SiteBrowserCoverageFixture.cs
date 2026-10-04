using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Owned reader inputs copied byte-for-byte from the completed Chrome session and Node builder.</summary>
internal sealed class SiteBrowserCoverageFixture : IDisposable
{
    private readonly SiteBrowserCoverageMetadata metadata;
    private readonly Dictionary<string, byte[]> browserFiles;

    private SiteBrowserCoverageFixture(string root, SiteCoverageSourceManifest manifest,
        SiteBrowserCoverageMetadata metadata, Dictionary<string, byte[]> browserFiles,
        string nodeName, byte[] nodeBytes, string sessionId)
    {
        Root = root;
        Manifest = manifest;
        this.metadata = metadata;
        this.browserFiles = browserFiles;
        NodeName = nodeName;
        NodeBytes = nodeBytes;
        SessionId = sessionId;
    }
    public string Root { get; }
    public SiteCoverageSourceManifest Manifest { get; }
    public string SessionId { get; }
    public string NodeName { get; }
    public byte[] NodeBytes { get; }
    public IReadOnlyDictionary<string, byte[]> BrowserFiles => browserFiles;
    public byte[] MetadataBytes { get; private set; } = [];

    public static async Task<SiteBrowserCoverageFixture> CreateAsync(string baseUrl, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        var coverageRoot = SiteCoverageArtifactFiles.RequiredCoverageRoot();
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(coverageRoot,
            SiteCoverageTokens.SourceManifestFile), cancellationToken);
        var manifest = JsonSerializer.Deserialize<SiteCoverageSourceManifest>(manifestBytes, SiteCoverageTokens.JsonOptions)
            ?? throw new InvalidDataException(SiteCoverageTokens.InvalidMetadataFailure);
        var origin = baseUrl.TrimEnd(SiteTokens.UrlPathSeparatorCharacter);
        var sessionsRoot = Path.Combine(coverageRoot, SiteCoverageTokens.BrowserDirectory,
            SiteCoverageTokens.SessionsDirectory);
        var matches = Directory.EnumerateDirectories(sessionsRoot).Select(path =>
            (Path: path, Metadata: ReadMetadata(path))).Where(item => item.Metadata.Origins.Contains(origin,
                StringComparer.Ordinal)).ToArray();
        if (matches.Length != SiteCoverageTokens.One)
        {
            throw new InvalidDataException(SiteCoverageTokens.InvalidMetadataFailure);
        }

        var selected = matches[SiteCoverageTokens.Zero];
        var node = await FindMappedNodeAsync(coverageRoot, inputs.Repository, manifest, cancellationToken);
        var root = Path.Combine(coverageRoot, SiteCoverageTokens.NativeEvidenceDirectory,
            SiteBrowserCoverageTokens.BrowserFixturePrefix + Guid.NewGuid().ToString(SiteCoverageTokens.CompactGuidFormat));
        var sessionId = Path.GetFileName(selected.Path);
        var browserFiles = selected.Metadata.CoverageFiles.ToDictionary(name => name,
            name => File.ReadAllBytes(Path.Combine(selected.Path, name)), StringComparer.Ordinal);
        var fixture = new SiteBrowserCoverageFixture(root, manifest, selected.Metadata, browserFiles,
            node.Name, node.Bytes, sessionId);
        try
        {
            fixture.MetadataBytes = await File.ReadAllBytesAsync(Path.Combine(selected.Path,
                SiteCoverageTokens.MetadataFile), cancellationToken);
            fixture.WriteAuthenticCopies();
            return fixture;
        }
        catch (Exception)
        {
            fixture.Dispose();
            throw;
        }
    }
    public string BrowserReceiptPath(string name) => BrowserReceiptPathFor(SessionId, name);

    public string BrowserReceiptPathFor(string sessionId, string name) => SiteCoverageArtifactFiles.RelativePath(
        SiteCoverageArtifactFiles.RequiredCoverageRoot(), Path.Combine(SessionPath(sessionId), name));

    public string MetadataPath(string sessionId) => SiteCoverageArtifactFiles.RelativePath(
        SiteCoverageArtifactFiles.RequiredCoverageRoot(), Path.Combine(SessionPath(sessionId),
            SiteCoverageTokens.MetadataFile));

    public string NodeReceiptPath => SiteCoverageArtifactFiles.RelativePath(
        SiteCoverageArtifactFiles.RequiredCoverageRoot(), Path.Combine(Root, SiteCoverageTokens.NodeDirectory, NodeName));

    public (string Name, byte[] Bytes) EmptyBrowserFile => browserFiles.Select(file =>
    {
        using var document = JsonDocument.Parse(file.Value, SiteCoverageTokens.JsonDocumentOptions);
        return (file.Key, file.Value, Empty: document.RootElement.GetProperty(SiteCoverageTokens.Result)
            .GetArrayLength() == SiteCoverageTokens.Zero);
    }).Where(file => file.Empty).Select(file => (file.Key, file.Value)).First();

    public byte[] AnonymousOnlyBytes()
    {
        foreach (var bytes in browserFiles.Values)
        {
            var root = JsonNode.Parse(bytes)!.AsObject();
            var anonymous = root[SiteCoverageTokens.Result]!.AsArray().Where(script =>
                script?[SiteCoverageTokens.Url]?.GetValue<string>().Length == SiteCoverageTokens.Zero)
                .Select(script => script!.DeepClone()).ToArray();
            if (anonymous.Length == SiteCoverageTokens.Zero)
            {
                continue;
            }

            root[SiteCoverageTokens.Result] = new JsonArray(anonymous);
            return JsonSerializer.SerializeToUtf8Bytes(root);
        }

        throw new InvalidDataException(SiteCoverageTokens.MissingNativeReceiptsFailure);
    }

    public byte[] ModifiedBrowserBytes(Action<JsonObject> modify)
    {
        var bytes = browserFiles.Values.First(value =>
        {
            using var document = JsonDocument.Parse(value, SiteCoverageTokens.JsonDocumentOptions);
            return document.RootElement.GetProperty(SiteCoverageTokens.Result).EnumerateArray().Any(script =>
                script.GetProperty(SiteCoverageTokens.Url).GetString()?.StartsWith(
                    SiteCoverageTokens.HttpScheme, StringComparison.Ordinal) == true);
        });
        var root = JsonNode.Parse(bytes)!.AsObject();
        modify(root);
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    public string AddSecondSession(byte[] bytes)
    {
        var id = SiteBrowserCoverageTokens.SecondSessionPrefix + Guid.NewGuid().ToString(SiteCoverageTokens.CompactGuidFormat);
        var path = SessionPath(id);
        Directory.CreateDirectory(path);
        var controlled = metadata with { CoverageFiles = [SiteBrowserCoverageTokens.RejectionFile] };
        File.WriteAllBytes(Path.Combine(path, SiteCoverageTokens.MetadataFile),
            JsonSerializer.SerializeToUtf8Bytes(controlled, SiteCoverageTokens.JsonOptions));
        File.WriteAllBytes(Path.Combine(path, SiteBrowserCoverageTokens.RejectionFile), bytes);
        return id;
    }

    public void ReplaceNodeWithEmptyResult()
    {
        var node = JsonNode.Parse(NodeBytes)!.AsObject();
        node[SiteCoverageTokens.Result] = new JsonArray();
        File.WriteAllBytes(Path.Combine(Root, SiteCoverageTokens.NodeDirectory, NodeName),
            JsonSerializer.SerializeToUtf8Bytes(node));
    }
    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private string SessionPath(string id) => Path.Combine(Root, SiteCoverageTokens.BrowserDirectory,
        SiteCoverageTokens.SessionsDirectory, id);

    private void WriteAuthenticCopies()
    {
        var nodeDirectory = Path.Combine(Root, SiteCoverageTokens.NodeDirectory);
        var browserDirectory = SessionPath(SessionId);
        Directory.CreateDirectory(nodeDirectory);
        Directory.CreateDirectory(browserDirectory);
        File.WriteAllBytes(Path.Combine(nodeDirectory, NodeName), NodeBytes);
        File.WriteAllBytes(Path.Combine(browserDirectory, SiteCoverageTokens.MetadataFile), MetadataBytes);
        foreach (var file in browserFiles)
        {
            File.WriteAllBytes(Path.Combine(browserDirectory, file.Key), file.Value);
        }
    }

    private static SiteBrowserCoverageMetadata ReadMetadata(string directory)
    {
        var bytes = File.ReadAllBytes(Path.Combine(directory, SiteCoverageTokens.MetadataFile));
        return JsonSerializer.Deserialize<SiteBrowserCoverageMetadata>(bytes, SiteCoverageTokens.JsonOptions)
            ?? throw new InvalidDataException(SiteCoverageTokens.InvalidMetadataFailure);
    }

    private static async Task<(string Name, byte[] Bytes)> FindMappedNodeAsync(string coverageRoot,
        string repository, SiteCoverageSourceManifest manifest, CancellationToken cancellationToken)
    {
        var urls = manifest.Sources.Select(source => new UriBuilder(SiteCoverageTokens.FileScheme,
            SiteCoverageTokens.Empty)
        {
            Path = Path.GetFullPath(Path.Combine(repository,
                source.Path.Replace(SiteCoverageTokens.RelativeSeparator, Path.DirectorySeparatorChar))),
        }.Uri.AbsoluteUri)
            .ToHashSet(StringComparer.Ordinal);
        var nodeRoot = Path.Combine(coverageRoot, SiteCoverageTokens.NodeDirectory);
        foreach (var path in Directory.EnumerateFiles(nodeRoot, SiteCoverageTokens.NodeCoverageFilePrefix +
                     SiteCoverageTokens.Wildcard + SiteCoverageTokens.JsonExtension).Order(StringComparer.Ordinal))
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            using var document = JsonDocument.Parse(bytes, SiteCoverageTokens.JsonDocumentOptions);
            if (document.RootElement.GetProperty(SiteCoverageTokens.Result).EnumerateArray().Any(script =>
                    urls.Contains(script.GetProperty(SiteCoverageTokens.Url).GetString() ?? SiteCoverageTokens.Empty)))
            {
                return (Path.GetFileName(path), bytes);
            }
        }

        throw new InvalidDataException(SiteCoverageTokens.MissingNativeReceiptsFailure);
    }
}
