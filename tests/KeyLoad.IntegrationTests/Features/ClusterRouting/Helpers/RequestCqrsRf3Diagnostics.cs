using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Captures only closed MCP rejection values from one actual C1 Aspire wave.</summary>
internal sealed class RequestCqrsRf3Diagnostics : IAsyncDisposable
{
    private const string MessagePrefix = "MCP transport rejected at ";
    private const string ArtifactDirectory = "qualification";
    private const string ArtifactRootDirectory = "artifacts";
    private const string ArtifactNamePrefix = "request-cqrs-rf3-mcp-rejections-";
    private const string ArtifactSuffix = ".json";
    private const string ArtifactDataKey = "KeyLoad.RequestCqrsRf3.McpRejectionArtifact";
    private const string InvalidWaveIdMessage = "The C1 diagnostics wave identifier must be nonempty.";
    private const string InvalidNodesMessage = "C1 diagnostics require the three actual RF3 node resources.";
    private const string OversizedArtifactMessage = "The bounded C1 MCP rejection artifact exceeded its byte limit.";
    private const string IncompleteSubscriptionMessage = "C1 diagnostics cannot be saved before every original subscription joins.";
    private const int MaximumRecordsPerNode = 32;
    private const int MaximumLineCharacters = 4_096;
    private const int MaximumArtifactBytes = 16 * 1_024;
    private const int ArtifactVersion = 1;

    private readonly Guid waveId;
    private readonly CancellationTokenSource lifetime = new();
    private readonly NodeCapture[] nodes;
    private readonly Task[] subscriptions;
    private readonly object disposalGate = new();
    private Task? disposalTask;
    private string? artifactPath;

    private RequestCqrsRf3Diagnostics(Guid waveId, NodeCapture[] nodes,
        ResourceLoggerService logger, ContainerResource[] resources)
    {
        this.waveId = waveId;
        this.nodes = nodes;
        subscriptions = resources.Select(resource => CaptureNodeAsync(logger, resource,
            nodes.Single(node => string.Equals(node.Name, resource.Name, StringComparison.Ordinal))))
            .ToArray();
    }

    internal static RequestCqrsRf3Diagnostics Start(Guid waveId, IEnumerable<ContainerResource> resources,
        ResourceLoggerService logger)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(logger);
        if (waveId == Guid.Empty)
        { throw new ArgumentException(InvalidWaveIdMessage, nameof(waveId)); }

        var captures = new[]
        {
            new NodeCapture(RequestCqrsRf3Protocol.Node1),
            new NodeCapture(RequestCqrsRf3Protocol.Node2),
            new NodeCapture(RequestCqrsRf3Protocol.Node3)
        };
        var nodeResources = resources.Where(resource => IsNode(resource.Name)).ToArray();
        if (nodeResources.Length != captures.Length
            || captures.Any(node => nodeResources.Count(resource => resource.Name == node.Name) != 1))
        { throw new InvalidOperationException(InvalidNodesMessage); }

        return new(waveId, captures, logger, nodeResources);
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
            if (disposalTask is not { IsCompleted: true } || subscriptions.Any(subscription => !subscription.IsCompleted))
            { throw new InvalidOperationException(IncompleteSubscriptionMessage); }
            return artifactPath ??= WriteArtifact();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            disposalTask ??= DisposeCoreAsync();
            return new(disposalTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(lifetime.CancelAsync, failures).ConfigureAwait(false);
        foreach (var subscription in subscriptions)
        { await ServerFailureObserver.ObserveAsync(() => subscription, failures).ConfigureAwait(false); }
        lifetime.Dispose();
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task CaptureNodeAsync(ResourceLoggerService logger, ContainerResource resource, NodeCapture node)
    {
        try
        {
            await foreach (var batch in logger.WatchAsync(resource)
                .WithCancellation(lifetime.Token).ConfigureAwait(false))
            {
                foreach (var line in batch)
                { node.TryCapture(line.Content, waveId); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
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

    private sealed class NodeCapture(string name)
    {
        private const string Separator = " for ";
        private readonly object gate = new();
        private readonly List<RequestCqrsRf3McpRejectionRecord> records = [];

        internal string Name { get; } = name;

        internal void TryCapture(string line, Guid waveId)
        {
            if (!TryParse(line, Name, waveId, out var record))
            { return; }
            lock (gate)
            {
                if (records.Count < MaximumRecordsPerNode)
                { records.Add(record); }
            }
        }

        internal RequestCqrsRf3McpRejectionRecord[] Snapshot()
        {
            lock (gate)
            { return [.. records]; }
        }

        private static bool TryParse(string line, string node, Guid waveId,
            out RequestCqrsRf3McpRejectionRecord record)
        {
            record = null!;
            if (line.Length > MaximumLineCharacters)
            { return false; }
            var marker = line.IndexOf(MessagePrefix, StringComparison.Ordinal);
            if (marker < 0 || line.IndexOf(MessagePrefix, marker + MessagePrefix.Length,
                StringComparison.Ordinal) >= 0)
            { return false; }

            var message = line.AsSpan(marker + MessagePrefix.Length).TrimEnd("\r\n");
            var separator = message.IndexOf(Separator, StringComparison.Ordinal);
            if (separator <= 0 || message[^1] != '.')
            { return false; }
            var stageText = message[..separator];
            var methodText = message[(separator + Separator.Length)..^1];
            if (!Enum.TryParse<McpTransportStage>(stageText, false, out var stage)
                || !Enum.IsDefined(stage)
                || !stageText.SequenceEqual(stage.ToString())
                || !Enum.TryParse<McpTransportMethodCategory>(methodText, false, out var method)
                || !Enum.IsDefined(method) || !methodText.SequenceEqual(method.ToString()))
            { return false; }

            record = new(waveId, node, stage, method);
            return true;
        }
    }
}
