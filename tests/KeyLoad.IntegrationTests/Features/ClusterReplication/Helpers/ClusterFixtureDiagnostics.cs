using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Owns RF3 resource log capture and bounded signed-discovery failure artifacts.</summary>
internal sealed class ClusterFixtureDiagnostics : IAsyncDisposable
{
    private const string ResourceStateUnavailable = "Resource state unavailable";
    private const string DiagnosticMessage = "Bounded RF3 diagnostics saved to {OutputPath} ({LineCount} lines).";
    private const string ResourceStateFormat = "Resource {0}, state {1}, exit {2}, health {3}";
    private const string DiagnosticLineFormat = "{0}: {1}";
    private const int SavedEventId = 100;
    private static readonly CompositeFormat ResourceStateTemplate = CompositeFormat.Parse(ResourceStateFormat);
    private static readonly CompositeFormat DiagnosticLineTemplate = CompositeFormat.Parse(DiagnosticLineFormat);
    private static readonly Action<ILogger, string, int, Exception?> LogSaved = LoggerMessage.Define<string, int>(
        LogLevel.Information, new EventId(SavedEventId), DiagnosticMessage);

    private readonly DistributedApplication app;
    private readonly ClusterFailureReceipts failureReceipts = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> nodeLogs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> nodeFailures = new(StringComparer.Ordinal);
    private const string RequestFailureMarker = "Database request failed";
    private const string RpcFailureMarker = "Orleans request stream failed";
    private const string MaintenanceFailureMarker = "Node-owned replica maintenance failed";
    private const int FailureLinesPerNode = 3;
    private const int MaximumCapturedLineBytes = 1_024;
    private readonly ConcurrentDictionary<string, Task> logCapture = new(StringComparer.Ordinal);
    private Task? resourceCapture;
    private readonly ConcurrentDictionary<string, NativeLogSubscriptionEvidence> logEvidence = new(StringComparer.Ordinal);

    /// <summary>Gets cancellation for the fixture-owned log subscriptions.</summary>
    internal CancellationToken LifetimeToken => lifetime.Token;

    /// <summary>Creates diagnostics bound to the actual Aspire application model.</summary>
    /// <param name="app">The running application whose resources and logs are observed.</param>
    internal ClusterFixtureDiagnostics(DistributedApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        this.app = app;
    }

    /// <summary>Starts subscriptions to the three managed RF3 node resources.</summary>
    /// <param name="logs">Aspire's resource log subscription service.</param>
    internal void Start(ResourceLoggerService logs)
    {
        ArgumentNullException.ThrowIfNull(logs);
        resourceCapture = CaptureResourcesAsync(logs);
    }

    /// <summary>Writes bounded resource states, signed discovery replies and recent node logs.</summary>
    /// <param name="peerSecret">The fixture's private shared discovery credential.</param>
    /// <param name="cancellationToken">The caller's test deadline, linked to the diagnostic request bound.</param>
    internal async Task SaveAsync(ReadOnlyMemory<byte> peerSecret, CancellationToken cancellationToken)
        => await SaveCoreAsync(peerSecret, null, cancellationToken).ConfigureAwait(false);

    internal Task SaveOwnedAsync(ReadOnlyMemory<byte> peerSecret, string output, CancellationToken cancellationToken)
        => SaveCoreAsync(peerSecret, output, cancellationToken);

    private async Task SaveCoreAsync(ReadOnlyMemory<byte> peerSecret, string? ownedOutput, CancellationToken cancellationToken)
    {
        var nodeGroups = new List<IEnumerable<string>>();
        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var name = ClusterFixtureProtocol.NodeName(number);
            var state = app.ResourceNotifications.TryGetCurrentState(name, out var current)
                ? string.Format(CultureInfo.InvariantCulture, ResourceStateTemplate, current.ResourceId,
                    current.Snapshot.State?.Text, current.Snapshot.ExitCode, current.Snapshot.HealthStatus)
                : ResourceStateUnavailable;
            var evidence = await new ClusterNodeHttpEvidence(app).ReadAsync(name, peerSecret, cancellationToken).ConfigureAwait(false);
            var lines = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, DiagnosticLineTemplate, name, state),
                string.Format(CultureInfo.InvariantCulture, DiagnosticLineTemplate, name, evidence.Readiness),
                string.Format(CultureInfo.InvariantCulture, DiagnosticLineTemplate, name, evidence.Discovery)
            };
            lines.AddRange(logEvidence.Values.Where(entry => entry.NodeName == name)
                .OrderByDescending(entry => entry.StartedAt).Take(1).Select(entry => entry.Snapshot()));
            lines.AddRange(ReadFailures(name).Select(line =>
                string.Format(CultureInfo.InvariantCulture, DiagnosticLineTemplate, name, line)));
            lines.AddRange(ReadTail(name).Reverse().Select(line =>
                string.Format(CultureInfo.InvariantCulture, DiagnosticLineTemplate, name, line)));
            nodeGroups.Add(lines);
        }

        var bounded = BoundedDiagnosticLog.BoundNodes(nodeGroups);
        var repository = FindRepositoryRoot();
        var output = Path.Combine(repository.FullName, ClusterFixtureProtocol.ArtifactDirectory,
            ClusterFixtureProtocol.QualificationDirectory);
        Directory.CreateDirectory(output);
        var path = ownedOutput is null ? failureReceipts.Save(output, bounded)
            : ClusterFailureReceipts.SaveImmutable(ownedOutput, bounded);
        LogSaved(app.Services.GetRequiredService<ILogger<ClusterFixtureDiagnostics>>(), path, bounded.Length, null);
    }

    /// <summary>Locates the solution root for the bounded qualification artifact.</summary>
    /// <returns>The nearest directory containing the solution marker.</returns>
    internal static DirectoryInfo FindRepositoryRoot()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository.Parent is not null
            && !File.Exists(Path.Combine(repository.FullName, ClusterFixtureProtocol.SolutionFileName)))
        {
            repository = repository.Parent;
        }

        return repository;
    }

    /// <summary>Cancels subscriptions and waits for their actual Aspire streams to finish.</summary>
    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        var cancelFailure = await OwnedProcessFailureObserver.CaptureAsync(lifetime.CancelAsync()).ConfigureAwait(false);
        if (cancelFailure is not null)
        { failures.Add(cancelFailure); }
        if (resourceCapture is not null)
        {
            var resourceFailure = await OwnedProcessFailureObserver.CaptureAsync(resourceCapture).ConfigureAwait(false);
            if (resourceFailure is not null)
            { failures.Add(resourceFailure); }
        }
        var logsFailure = await OwnedProcessFailureObserver.CaptureAsync(Task.WhenAll(logCapture.Values)).ConfigureAwait(false);
        if (logsFailure is not null)
        { failures.Add(logsFailure); }
        lifetime.Dispose();
        if (failures.Count == 1)
        { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1)
        { throw new AggregateException("Native diagnostic subscriptions and disposal failed.", failures); }
    }

    private async Task CaptureResourcesAsync(ResourceLoggerService logs)
    {
        try
        {
            await foreach (var change in app.ResourceNotifications.WatchAsync(lifetime.Token).ConfigureAwait(false))
            {
                if (ClusterFixtureProtocol.IsNodeName(change.Resource.Name))
                {
                    _ = logCapture.GetOrAdd(change.ResourceId,
                        id => CaptureLogsAsync(logs, id, change.Resource.Name));
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task CaptureLogsAsync(ResourceLoggerService logs, string resourceId, string name)
    {
        var observation = logEvidence.GetOrAdd(resourceId, _ => new NativeLogSubscriptionEvidence(name));
        var buffer = nodeLogs.GetOrAdd(name, _ => new());
        var failures = nodeFailures.GetOrAdd(name, _ => new());
        try
        {
            await foreach (var batch in logs.WatchAsync(resourceId).WithCancellation(lifetime.Token).ConfigureAwait(false))
            {
                foreach (var line in batch)
                {
                    observation.Received();
                    CaptureLogLine(buffer, failures, line.Content);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        { observation.Terminal(NativeLogSubscriptionPhase.Canceled); }
        catch (Exception)
        {
            observation.Terminal(NativeLogSubscriptionPhase.Faulted);
            throw;
        }
        finally
        { observation.Terminal(NativeLogSubscriptionPhase.Completed); }
    }

    private string[] ReadTail(string name) => nodeLogs.TryGetValue(name, out var buffer) ? buffer.ToArray() : [];
    private string[] ReadFailures(string name) => nodeFailures.TryGetValue(name, out var buffer) ? buffer.ToArray() : [];

    private static void CaptureLogLine(ConcurrentQueue<string> buffer, ConcurrentQueue<string> failures, string line)
    {
        var content = BoundedDiagnosticLog.ClipUtf8(line, MaximumCapturedLineBytes);
        AppendLogLine(buffer, content, ClusterFixtureProtocol.CapturedLogLinesPerNode);
        if (content.Contains(RequestFailureMarker, StringComparison.Ordinal)
            || content.Contains(RpcFailureMarker, StringComparison.Ordinal)
            || content.Contains(MaintenanceFailureMarker, StringComparison.Ordinal))
        {
            AppendLogLine(failures, content, FailureLinesPerNode);
        }
    }

    private static void AppendLogLine(ConcurrentQueue<string> buffer, string line, int maximumLines)
    {
        buffer.Enqueue(line);
        while (buffer.Count > maximumLines)
        {
            buffer.TryDequeue(out _);
        }
    }

}
