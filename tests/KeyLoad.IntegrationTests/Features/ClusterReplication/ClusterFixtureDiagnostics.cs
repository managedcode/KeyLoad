using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Owns RF3 resource log capture and bounded signed-discovery failure artifacts.</summary>
internal sealed class ClusterFixtureDiagnostics : IAsyncDisposable
{
    private static readonly TimeSpan DiagnosticRequestTimeout = TimeSpan.FromSeconds(3);
    private const int MaximumResponseBytes = 8 * 1_024;
    private const string DiscoveryUnavailable = "Signed peer discovery unavailable.";
    private const string OversizedResponse = "Signed discovery response exceeds the diagnostic byte limit.";
    private const string ResourceStateUnavailable = "Resource state unavailable";
    private const string DiagnosticMessage = "Bounded RF3 diagnostics saved to {OutputPath} ({LineCount} lines).";
    private const string ResourceStateFormat = "Resource {0}, state {1}, exit {2}, health {3}";
    private const string DiagnosticLineFormat = "{0}: {1}";
    private const string HttpStatusFormat = "{0} HTTP {1}.";
    private const int SavedEventId = 100;
    private static readonly CompositeFormat ResourceStateTemplate = CompositeFormat.Parse(ResourceStateFormat);
    private static readonly CompositeFormat DiagnosticLineTemplate = CompositeFormat.Parse(DiagnosticLineFormat);
    private static readonly CompositeFormat HttpStatusTemplate = CompositeFormat.Parse(HttpStatusFormat);
    private static readonly Action<ILogger, string, int, Exception?> LogSaved = LoggerMessage.Define<string, int>(
        LogLevel.Information, new EventId(SavedEventId), DiagnosticMessage);

    private readonly DistributedApplication app;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> nodeLogs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> logCapture = new(StringComparer.Ordinal);
    private Task? resourceCapture;

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
    {
        var lines = new List<string>();
        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var name = ClusterFixtureProtocol.NodeName(number);
            var state = app.ResourceNotifications.TryGetCurrentState(name, out var current)
                ? string.Format(CultureInfo.InvariantCulture, ResourceStateTemplate, current.ResourceId,
                    current.Snapshot.State?.Text, current.Snapshot.ExitCode, current.Snapshot.HealthStatus)
                : ResourceStateUnavailable;
            var discovery = await ReadDiscoveryAsync(name, peerSecret, cancellationToken).ConfigureAwait(false);
            lines.Add(string.Format(CultureInfo.InvariantCulture, DiagnosticLineTemplate, name, state));
            lines.Add(string.Format(CultureInfo.InvariantCulture, DiagnosticLineTemplate, name, discovery));
            lines.AddRange(ReadTail(name).Select(line =>
                string.Format(CultureInfo.InvariantCulture, DiagnosticLineTemplate, name, line)));
        }

        var bounded = BoundedDiagnosticLog.Bound(lines);
        var repository = FindRepositoryRoot();
        var output = Path.Combine(repository.FullName, ClusterFixtureProtocol.ArtifactDirectory,
            ClusterFixtureProtocol.QualificationDirectory);
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, ClusterFixtureProtocol.DiagnosticsFileName);
        File.WriteAllLines(path, bounded);
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
        await lifetime.CancelAsync().ConfigureAwait(false);
        if (resourceCapture is not null)
        {
            await resourceCapture.ConfigureAwait(false);
        }

        await Task.WhenAll(logCapture.Values).ConfigureAwait(false);
        lifetime.Dispose();
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
        var buffer = nodeLogs.GetOrAdd(name, _ => new());
        try
        {
            await foreach (var batch in logs.WatchAsync(resourceId).WithCancellation(lifetime.Token).ConfigureAwait(false))
            {
                foreach (var line in batch)
                {
                    AppendLogLine(buffer, line.Content);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private string[] ReadTail(string name) => nodeLogs.TryGetValue(name, out var buffer) ? buffer.ToArray() : [];

    private static void AppendLogLine(ConcurrentQueue<string> buffer, string line)
    {
        buffer.Enqueue(line);
        while (buffer.Count > ClusterFixtureProtocol.CapturedLogLinesPerNode)
        {
            buffer.TryDequeue(out _);
        }
    }

    private async Task<string> ReadDiscoveryAsync(string name, ReadOnlyMemory<byte> peerSecret,
        CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(DiagnosticRequestTimeout);
            using var http = new HttpClient(new PeerSecurity(peerSecret, TimeProvider.System).CreateHandler())
            { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(app.GetEndpoint(name, ClusterFixtureProtocol.HttpEndpointName), ReplicaProtocol.DiscoveryPath));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            return response.StatusCode == System.Net.HttpStatusCode.OK
                ? await ReadBoundedTextAsync(response.Content, deadline.Token).ConfigureAwait(false)
                : string.Format(CultureInfo.InvariantCulture, HttpStatusTemplate, DiscoveryUnavailable,
                    (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DiscoveryUnavailable;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException or KeyLoad.KeyLoadException)
        {
            return DiscoveryUnavailable;
        }
    }

    private static async Task<string> ReadBoundedTextAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
        {
            return OversizedResponse;
        }

        var bytes = new byte[MaximumResponseBytes + 1];
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return Encoding.UTF8.GetString(bytes, 0, count);
            }

            count += read;
        }

        return OversizedResponse;
    }

}
