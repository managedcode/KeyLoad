using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterFixtureReceiptWriter
{
    private const string AssemblyName = "KeyLoad.Server";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<string> WriteAsync(NativeCoverageClusterFixtureContext context,
        ClusterFixtureSourceImage sourceImage, IReadOnlyCollection<NativeCoverageClusterCaseIdentity> executedCases,
        IReadOnlyDictionary<string, ContainerRuntimeInspection> stoppedNodes,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidFixture);
        }
        if (!CasesMatch(context.SelectedCases, executedCases)
            || stoppedNodes.Count != ClusterFixtureProtocol.NodeCount)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidFixture);
        }
        var nodes = await ReadNodesAsync(context, stoppedNodes, options, cancellationToken).ConfigureAwait(false);
        var materializerPath = Path.Combine(context.EvidenceRoot,
            NativeCoverageClusterFixtureProtocol.MaterializerReceiptName);
        var inspectPath = Path.Combine(context.EvidenceRoot,
            NativeCoverageClusterFixtureProtocol.ImageIdReceiptName);
        var materializer = await ReferenceAsync(materializerPath, context, options, cancellationToken).ConfigureAwait(false);
        var inspect = await ReferenceAsync(inspectPath, context, options, cancellationToken).ConfigureAwait(false);
        var receipt = new NativeCoverageClusterFixtureReceipt(
            NativeCoverageClusterFixtureProtocol.SchemaVersion, context.FixtureId, context.RunId,
            NativeCoverageClusterFixtureProtocol.ClusterSuite, context.SourceRevision, context.SourceManifestSha256,
            [.. executedCases.OrderBy(item => item.ClassName, StringComparer.Ordinal)
                .ThenBy(item => item.MethodName, StringComparer.Ordinal)],
            new(sourceImage.Reference, sourceImage.ManifestDigest, sourceImage.ReceiptSha256),
            new(context.ImageReference, context.ImageId, context.ContextManifestSha256,
                context.DockerfileSha256, materializer, inspect),
            new(AssemblyName, context.Server.Mvid, context.Server.DllSha256, context.Server.PdbSha256,
                context.Server.SourceReceiptSha256),
            new(context.Collector.PackageId, context.Collector.Version, context.Collector.ClosureDigest,
                context.Collector.SettingsSha256, context.Bounds), nodes);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions);
        if (bytes.Length > NativeCoverageClusterFixtureProtocol.MaximumReceiptBytes)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidFixture);
        }
        var path = Path.Combine(context.EvidenceRoot, NativeCoverageClusterFixtureProtocol.FixtureReceiptName);
        await WriteCreateOnlyAsync(path, bytes, execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static async Task<IReadOnlyList<NativeCoverageClusterNodeReceipt>> ReadNodesAsync(
        NativeCoverageClusterFixtureContext context,
        IReadOnlyDictionary<string, ContainerRuntimeInspection> stoppedNodes,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken token)
    {
        var nodes = new List<NativeCoverageClusterNodeReceipt>(ClusterFixtureProtocol.NodeCount);
        for (var number = ClusterFixtureProtocol.FirstNodeNumber; number <= ClusterFixtureProtocol.NodeCount; number++)
        {
            var name = ClusterFixtureProtocol.NodeName(number);
            if (!stoppedNodes.TryGetValue(name, out var inspection))
            {
                throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidFixture);
            }
            nodes.Add(await NativeCoverageClusterNodeReceiptBuilder.ReadAsync(name, inspection, context, options, token)
                .ConfigureAwait(false));
        }
        return nodes;
    }

    private static bool CasesMatch(IReadOnlyCollection<NativeCoverageClusterCaseIdentity> expected,
        IReadOnlyCollection<NativeCoverageClusterCaseIdentity> actual)
    {
        if (actual.Count != expected.Count || actual.Distinct().Count() != actual.Count)
        {
            return false;
        }
        return new HashSet<NativeCoverageClusterCaseIdentity>(expected).SetEquals(actual);
    }

    private static async Task<NativeCoverageClusterArtifactReference> ReferenceAsync(string path,
        NativeCoverageClusterFixtureContext context, IOptions<NativeCoverageExecutionOptions> options,
        CancellationToken token)
    {
        var bytes = await NativeCoverageClusterBoundedFileReader.ReadAsync(path,
            NativeCoverageClusterFixtureProtocol.MaximumReceiptBytes, options, token).ConfigureAwait(false);
        var relative = Path.GetRelativePath(context.EvidenceRoot, Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidFixture);
        }
        return new(relative.Replace(Path.DirectorySeparatorChar, '/'), bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static async Task WriteCreateOnlyAsync(string path, byte[] bytes, int bufferBytes, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferBytes,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        FlushToDisk(stream);
    }

    private static void FlushToDisk(FileStream stream) => stream.Flush(flushToDisk: true);
}
