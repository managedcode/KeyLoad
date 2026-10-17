using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3FixtureReceiptWriter
{
    private const string AssemblyName = "KeyLoad.Server";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<string> WriteAsync(NativeCoverageRf3FixtureContext context,
        ClusterFixtureSourceImage sourceImage, IReadOnlyCollection<NativeCoverageRf3CaseIdentity> executedCases,
        IReadOnlyDictionary<string, ContainerRuntimeInspection> stoppedNodes,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidFixture);
        }
        if (!CasesMatch(context.SelectedCases, executedCases)
            || stoppedNodes.Count != ClusterFixtureProtocol.NodeCount)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidFixture);
        }
        var nodes = await ReadNodesAsync(context, stoppedNodes, options, cancellationToken).ConfigureAwait(false);
        var materializerPath = Path.Combine(context.EvidenceRoot,
            NativeCoverageRf3FixtureProtocol.MaterializerReceiptName);
        var inspectPath = Path.Combine(context.EvidenceRoot,
            NativeCoverageRf3FixtureProtocol.ImageIdReceiptName);
        var materializer = await ReferenceAsync(materializerPath, context, options, cancellationToken).ConfigureAwait(false);
        var inspect = await ReferenceAsync(inspectPath, context, options, cancellationToken).ConfigureAwait(false);
        var receipt = new NativeCoverageRf3FixtureReceipt(
            NativeCoverageRf3FixtureProtocol.SchemaVersion, context.FixtureId, context.RunId,
            NativeCoverageRf3FixtureProtocol.Rf3Suite, context.SourceRevision, context.SourceManifestSha256,
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
        if (bytes.Length > NativeCoverageRf3FixtureProtocol.MaximumReceiptBytes)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidFixture);
        }
        var path = Path.Combine(context.EvidenceRoot, NativeCoverageRf3FixtureProtocol.FixtureReceiptName);
        await WriteCreateOnlyAsync(path, bytes, execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static async Task<IReadOnlyList<NativeCoverageRf3NodeReceipt>> ReadNodesAsync(
        NativeCoverageRf3FixtureContext context,
        IReadOnlyDictionary<string, ContainerRuntimeInspection> stoppedNodes,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken token)
    {
        var nodes = new List<NativeCoverageRf3NodeReceipt>(ClusterFixtureProtocol.NodeCount);
        for (var number = ClusterFixtureProtocol.FirstNodeNumber; number <= ClusterFixtureProtocol.NodeCount; number++)
        {
            var name = ClusterFixtureProtocol.NodeName(number);
            if (!stoppedNodes.TryGetValue(name, out var inspection))
            {
                throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidFixture);
            }
            nodes.Add(await NativeCoverageRf3NodeReceiptBuilder.ReadAsync(name, inspection, context, options, token)
                .ConfigureAwait(false));
        }
        return nodes;
    }

    private static bool CasesMatch(IReadOnlyCollection<NativeCoverageRf3CaseIdentity> expected,
        IReadOnlyCollection<NativeCoverageRf3CaseIdentity> actual)
    {
        if (actual.Count != expected.Count || actual.Distinct().Count() != actual.Count)
        {
            return false;
        }
        return new HashSet<NativeCoverageRf3CaseIdentity>(expected).SetEquals(actual);
    }

    private static async Task<NativeCoverageRf3ArtifactReference> ReferenceAsync(string path,
        NativeCoverageRf3FixtureContext context, IOptions<NativeCoverageExecutionOptions> options,
        CancellationToken token)
    {
        var bytes = await NativeCoverageRf3BoundedFileReader.ReadAsync(path,
            NativeCoverageRf3FixtureProtocol.MaximumReceiptBytes, options, token).ConfigureAwait(false);
        var relative = Path.GetRelativePath(context.EvidenceRoot, Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidFixture);
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
