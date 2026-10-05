using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Captures only closed MCP rejection values from one actual C1 Aspire wave.</summary>
internal sealed class RequestCqrsRf3Diagnostics : IAsyncDisposable
{
    private const string ArtifactDirectory = "qualification";
    private const string ArtifactRootDirectory = "artifacts";
    private const string ArtifactNamePrefix = "request-cqrs-rf3-mcp-rejections-";
    private const string ArtifactSuffix = ".json";
    private const string ArtifactDataKey = "KeyLoad.RequestCqrsRf3.McpRejectionArtifact";
    private const string InvalidWaveIdMessage = "The C1 diagnostics wave identifier must be nonempty.";
    private const string InvalidNodesMessage = "C1 diagnostics require the three actual RF3 node resources.";
    private const string OversizedArtifactMessage = "The bounded C1 MCP rejection artifact exceeded its byte limit.";
    private const string IncompleteSubscriptionMessage = "C1 diagnostics cannot be saved before every original subscription joins.";
    private const int MaximumArtifactBytes = 16 * 1_024;
    private const int ArtifactVersion = 1;

    private readonly Guid waveId;
    private readonly RequestCqrsRf3McpRejectionNodeCapture[] nodes;
    private readonly RequestCqrsRf3DiagnosticsCleanup cleanup;
    private readonly System.Threading.Lock disposalGate = new();
    private Task? disposalTask;
    private string? artifactPath;

    private RequestCqrsRf3Diagnostics(Guid waveId, RequestCqrsRf3McpRejectionNodeCapture[] nodes,
        ResourceLoggerService logger, ContainerResource[] resources,
        Action<RequestCqrsLifecycleStage>? failureObserver)
    {
        this.waveId = waveId;
        this.nodes = nodes;
        cleanup = new(waveId, logger, resources, nodes, failureObserver);
    }

    internal static RequestCqrsRf3Diagnostics Start(Guid waveId, IEnumerable<ContainerResource> resources,
        ResourceLoggerService logger, Action<RequestCqrsLifecycleStage>? failureObserver = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(logger);
        if (waveId == Guid.Empty)
        { throw new ArgumentException(InvalidWaveIdMessage, nameof(waveId)); }

        var captures = new[]
        {
            new RequestCqrsRf3McpRejectionNodeCapture(RequestCqrsRf3Protocol.Node1),
            new RequestCqrsRf3McpRejectionNodeCapture(RequestCqrsRf3Protocol.Node2),
            new RequestCqrsRf3McpRejectionNodeCapture(RequestCqrsRf3Protocol.Node3)
        };
        var nodeResources = resources.Where(resource => IsNode(resource.Name)).ToArray();
        if (nodeResources.Length != captures.Length
            || captures.Any(node => nodeResources.Count(resource => resource.Name == node.Name) != 1))
        { throw new InvalidOperationException(InvalidNodesMessage); }

        return new(waveId, captures, logger, nodeResources, failureObserver);
    }

    internal void SaveFailureEvidence(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var path = SaveEvidence();
        failure.Data[ArtifactDataKey] = path;
    }

    internal string SaveEvidence()
    {
        lock (disposalGate)
        {
            if (disposalTask is not { IsCompleted: true } || !cleanup.IsJoined)
            { throw new InvalidOperationException(IncompleteSubscriptionMessage); }
            return artifactPath ??= WriteArtifact();
        }
    }

    internal RequestCqrsCaptureLifecycleSnapshot ReadLifecycleSnapshot()
        => cleanup.ReadLifecycleSnapshot();

    internal Task WaitForRecordAsync(string node, McpTransportStage stage,
        McpTransportMethodCategory methodCategory, CancellationToken cancellationToken)
    {
        var capture = nodes.SingleOrDefault(candidate => string.Equals(candidate.Name, node, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(nameof(node));
        return capture.WaitForRecordAsync(stage, methodCategory, cancellationToken);
    }

    internal Task CompleteAndDrainAsync(CancellationToken cancellationToken)
    {
        lock (disposalGate)
        {
            disposalTask ??= cleanup.CompleteAndDrainAsync(cancellationToken);
            return disposalTask;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            disposalTask ??= cleanup.DisposeAsync().AsTask();
            return new(disposalTask);
        }
    }

    private string WriteArtifact()
    {
        var records = nodes.SelectMany(node => node.Snapshot()).ToArray();
        var artifact = new RequestCqrsRf3McpRejectionArtifact(ArtifactVersion, waveId, records);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact);
        if (bytes.Length > MaximumArtifactBytes)
        { throw new InvalidOperationException(OversizedArtifactMessage); }

        var root = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var directory = Path.Combine(root, ArtifactRootDirectory, ArtifactDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ArtifactNamePrefix + waveId.ToString("N") + ArtifactSuffix);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        return path;
    }

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;
}
