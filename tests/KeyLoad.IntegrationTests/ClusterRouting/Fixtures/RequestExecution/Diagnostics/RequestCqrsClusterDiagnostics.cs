using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Captures only closed MCP rejection values from one actual C1 Aspire wave.</summary>
internal sealed class RequestCqrsClusterDiagnostics : IAsyncDisposable
{
    private const string ArtifactDirectory = "qualification";
    private const string ArtifactRootDirectory = "artifacts";
    private const string ArtifactNamePrefix = "request-cqrs-cluster-mcp-rejections-";
    private const string RpcArtifactNamePrefix = "request-cqrs-cluster-rpc-failures-";
    private const string RpcArtifactDataKey = "KeyLoad.RequestCqrsCluster.RpcFailureArtifact";
    private const string ConnectionCancellationDataKey = "KeyLoad.RequestCqrsCluster.ConnectionCancellationArtifact";
    private const string ArtifactSuffix = ".json";
    private const string ArtifactDataKey = "KeyLoad.RequestCqrsCluster.McpRejectionArtifact";
    private const string InvalidWaveIdMessage = "The C1 diagnostics wave identifier must be nonempty.";
    private const string InvalidNodesMessage = "C1 diagnostics require the three actual Orleans node resources.";
    private const string OversizedArtifactMessage = "The bounded C1 MCP rejection artifact exceeded its byte limit.";
    private const string IncompleteSubscriptionMessage = "C1 diagnostics cannot be saved before every original subscription joins.";
    private const int MaximumArtifactBytes = 16 * 1_024;
    private const int ArtifactVersion = 1;
    private const int RpcArtifactVersion = 2;
    private const string RpcReadbackMessage = "The owned RPC diagnostic export did not retain the complete original records.";

    private readonly Guid waveId;
    private readonly RequestCqrsClusterMcpRejectionNodeCapture[] nodes;
    private readonly RequestCqrsClusterDiagnosticsCleanup cleanup;
    private readonly System.Threading.Lock disposalGate = new();
    private Task? disposalTask;
    private string? artifactPath;
    private string? rpcArtifactPath;
    private string? connectionCancellationArtifactPath;

    private RequestCqrsClusterDiagnostics(Guid waveId, RequestCqrsClusterMcpRejectionNodeCapture[] nodes,
        ResourceLoggerService logger, ContainerResource[] resources,
        Action<RequestCqrsLifecycleStage>? failureObserver,
        RequestCqrsClusterSubscriberTransitionHooks? subscriberTransitions)
    {
        this.waveId = waveId;
        this.nodes = nodes;
        cleanup = new(waveId, logger, resources, nodes, failureObserver, subscriberTransitions);
    }

    internal static RequestCqrsClusterDiagnostics Start(Guid waveId, IEnumerable<ContainerResource> resources,
        ResourceLoggerService logger, Action<RequestCqrsLifecycleStage>? failureObserver = null,
        RequestCqrsClusterSubscriberTransitionHooks? subscriberTransitions = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(logger);
        if (waveId == Guid.Empty)
        {
            throw new ArgumentException(InvalidWaveIdMessage, nameof(waveId));
        }

        var captures = new[]
        {
            new RequestCqrsClusterMcpRejectionNodeCapture(RequestCqrsClusterProtocol.Node1),
            new RequestCqrsClusterMcpRejectionNodeCapture(RequestCqrsClusterProtocol.Node2),
            new RequestCqrsClusterMcpRejectionNodeCapture(RequestCqrsClusterProtocol.Node3)
        };
        var nodeResources = resources.Where(resource => IsNode(resource.Name)).ToArray();
        if (nodeResources.Length != captures.Length
            || captures.Any(node => nodeResources.Count(resource => resource.Name == node.Name) != 1))
        {
            throw new InvalidOperationException(InvalidNodesMessage);
        }

        return new(waveId, captures, logger, nodeResources, failureObserver, subscriberTransitions);
    }

    internal void SaveFailureEvidence(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var path = SaveEvidence();
        failure.Data[ArtifactDataKey] = path;
        lock (disposalGate)
        {
            failure.Data[RpcArtifactDataKey] = rpcArtifactPath ??= WriteRpcArtifact();
            failure.Data[ConnectionCancellationDataKey] = connectionCancellationArtifactPath
                ??= RequestCqrsConnectionCancellationArtifactWriter.Write(waveId,
                    nodes.Select(node => node.ConnectionCancellationSnapshot()).ToArray());
        }
    }

    internal string SaveEvidence()
    {
        lock (disposalGate)
        {
            if (disposalTask is not { IsCompleted: true } || !cleanup.IsJoined)
            {
                throw new InvalidOperationException(IncompleteSubscriptionMessage);
            }
            if (artifactPath is null)
            {
                artifactPath = WriteArtifact();
                RequestCqrsNativeCaptureArtifact.Write(artifactPath, waveId,
                    nodes.Select(node => node.NativeSnapshot()).ToArray());
            }
            return artifactPath;
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
        var artifact = new RequestCqrsClusterMcpRejectionArtifact(ArtifactVersion, waveId, records);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact);
        if (bytes.Length > MaximumArtifactBytes)
        {
            throw new InvalidOperationException(OversizedArtifactMessage);
        }

        var root = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var directory = Path.Combine(root, ArtifactRootDirectory, ArtifactDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ArtifactNamePrefix + waveId.ToString("N") + ArtifactSuffix);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        return path;
    }

    private string WriteRpcArtifact()
    {
        var records = nodes.SelectMany(node => node.RpcSnapshot()).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new RequestCqrsRpcFailureArtifact(RpcArtifactVersion,
            waveId, records));
        if (bytes.Length > MaximumArtifactBytes)
        {
            throw new InvalidOperationException(OversizedArtifactMessage);
        }
        var directory = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            ArtifactRootDirectory, ArtifactDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, RpcArtifactNamePrefix + waveId.ToString("N") + ArtifactSuffix);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        VerifyRpcExport(path, bytes, records);
        return path;
    }

    private void VerifyRpcExport(string path, byte[] original, RequestCqrsRpcFailureRecord[] records)
    {
        var saved = new byte[original.Length];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.ReadExactly(saved);
        if (stream.ReadByte() != -1 || !saved.AsSpan().SequenceEqual(original))
        {
            throw new InvalidOperationException(RpcReadbackMessage);
        }
        var result = JsonSerializer.Deserialize<RequestCqrsRpcFailureArtifact>(saved);
        if (result is null || result.Version != RpcArtifactVersion || result.WaveId != waveId
            || result.Failures is null || !result.Failures.SequenceEqual(records))
        {
            throw new InvalidOperationException(RpcReadbackMessage);
        }
    }

    private static bool IsNode(string name) => name is RequestCqrsClusterProtocol.Node1
        or RequestCqrsClusterProtocol.Node2 or RequestCqrsClusterProtocol.Node3;
}
