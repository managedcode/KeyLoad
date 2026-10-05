using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal sealed class RequestCqrsRf3DiagnosticsTestScope(Guid waveId) : IAsyncDisposable
{
    private readonly HashSet<string> completedResourceNames = new(StringComparer.Ordinal);
    private CancellationTokenSource? subscriberLifetime;
    private DistributedApplication? application;
    private RequestCqrsRf3Diagnostics? diagnostics;
    private ResourceLoggerService? resourceLogger;
    private ContainerResource[] resources = [];
    private IAsyncEnumerator<LogSubscriber>? subscriberEnumerator;
    private Task<bool>? pendingSubscriberMove;
    private string? dataRoot;
    private string? artifactPath;
    private bool diagnosticsJoined;
    private bool subscriberObserverJoined;
    private Task? subscriberStopTask;

    internal Guid WaveId { get; } = waveId;
    internal RequestCqrsRf3Diagnostics Capture => diagnostics
        ?? throw new InvalidOperationException("The diagnostics owner is absent.");
    internal string OwnedArtifactPath => artifactPath
        ?? throw new InvalidOperationException("The owned artifact path is absent.");

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        dataRoot = RequestCqrsRf3DiagnosticsArtifactFiles.CreateDataRoot(WaveId);
        var args = RequestCqrsRf3DiagnosticsArtifactFiles.CreateAppHostArguments(dataRoot);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
            cancellationToken).ConfigureAwait(false);
        resources = SelectNodeResources(builder);
        var ownedApplication = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        application = ownedApplication;
        var loggerService = ownedApplication.Services.GetRequiredService<ResourceLoggerService>();
        resourceLogger = loggerService;
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        subscriberLifetime = lifetime;
        subscriberEnumerator = loggerService.WatchAnySubscribersAsync(lifetime.Token)
            .GetAsyncEnumerator();
        pendingSubscriberMove = subscriberEnumerator.MoveNextAsync().AsTask();
        diagnostics = RequestCqrsRf3Diagnostics.Start(WaveId, resources, loggerService);
        artifactPath = RequestCqrsRf3DiagnosticsArtifactFiles.ExpectedPath(WaveId);
        await WaitForSubscriberStateAsync(true).ConfigureAwait(false);
    }

    internal void EmitMalformedThenOneValidPerNode()
    {
        foreach (var resource in resources)
        {
            var logger = resourceLogger!.GetLogger(resource);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitMalformed(logger);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitValid(logger);
        }
    }

    internal void EmitFortyValidLinesPerNode()
    {
        foreach (var resource in resources)
        {
            var logger = resourceLogger!.GetLogger(resource);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitFortyValid(logger);
        }
    }

    internal async Task<byte[]> CompleteAndReadArtifactAsync(CancellationToken cancellationToken)
    {
        await JoinOriginalSubscriptionsAsync().ConfigureAwait(false);
        return await RequestCqrsRf3DiagnosticsArtifactFiles.WriteAndReadAsync(diagnostics!, artifactPath!,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task JoinOriginalSubscriptionsAsync()
    {
        CompleteResourceStreams();
        await WaitForSubscriberStateAsync(false).ConfigureAwait(false);
        await JoinDiagnosticsTwiceAsync().ConfigureAwait(false);
        try
        { await StopSubscriberObserverAsync().ConfigureAwait(false); }
        finally
        { subscriberObserverJoined = true; }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        CompleteResourceStreams(failures);
        if (!diagnosticsJoined && diagnostics is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => diagnostics.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
            diagnosticsJoined = true;
        }
        if (!subscriberObserverJoined)
        {
            await ServerFailureObserver.ObserveAsync(StopSubscriberObserverAsync, failures).ConfigureAwait(false);
            subscriberObserverJoined = true;
        }
        var beforeApplicationDispose = failures.Count;
        var ownedApplication = application;
        if (ownedApplication is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => ownedApplication.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
        }
        RequestCqrsRf3DiagnosticsArtifactFiles.DeleteOwned(artifactPath, failures);
        if (failures.Count == beforeApplicationDispose && dataRoot is not null && Directory.Exists(dataRoot))
        { ServerFailureObserver.Observe(() => Directory.Delete(dataRoot, recursive: true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task WaitForSubscriberStateAsync(bool expected)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        while (observed.Count < RequestCqrsRf3Protocol.NodeCount)
        {
            var enumerator = subscriberEnumerator
                ?? throw new InvalidOperationException("The Aspire subscriber observer is not initialized.");
            var pending = pendingSubscriberMove;
            pendingSubscriberMove = null;
            var moved = pending is null
                ? await enumerator.MoveNextAsync().ConfigureAwait(false)
                : await pending.ConfigureAwait(false);
            if (!moved)
            { throw new InvalidOperationException("Aspire ended its resource subscriber observation unexpectedly."); }
            var subscriber = enumerator.Current;
            if (IsNode(subscriber.Name) && subscriber.AnySubscribers == expected)
            { observed.Add(subscriber.Name); }
        }
    }

    private void CompleteResourceStreams()
    {
        var failures = new List<Exception>();
        CompleteResourceStreams(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void CompleteResourceStreams(List<Exception> failures)
    {
        var logger = resourceLogger;
        if (logger is null)
        { return; }
        foreach (var resource in resources)
        {
            if (completedResourceNames.Contains(resource.Name))
            { continue; }
            var before = failures.Count;
            ServerFailureObserver.Observe(() => logger.Complete(resource), failures);
            if (failures.Count == before)
            { completedResourceNames.Add(resource.Name); }
        }
    }

    private async Task JoinDiagnosticsTwiceAsync()
    {
        var capture = diagnostics ?? throw new InvalidOperationException("The Aspire diagnostics owner is absent.");
        var first = capture.DisposeAsync().AsTask();
        var repeated = capture.DisposeAsync().AsTask();
        if (!ReferenceEquals(first, repeated))
        { throw new InvalidOperationException("Repeated diagnostics disposal did not share one completion task."); }
        await first.ConfigureAwait(false);
        diagnosticsJoined = true;
    }

    private Task StopSubscriberObserverAsync()
        => subscriberStopTask ??= StopSubscriberObserverCoreAsync();

    private async Task StopSubscriberObserverCoreAsync()
    {
        var failures = new List<Exception>();
        var lifetime = subscriberLifetime;
        if (lifetime is not null)
        { await ServerFailureObserver.ObserveAsync(lifetime.CancelAsync, failures).ConfigureAwait(false); }
        var pending = pendingSubscriberMove;
        pendingSubscriberMove = null;
        if (pending is not null)
        { await ServerFailureObserver.ObserveAsync(() => pending, failures).ConfigureAwait(false); }
        var enumerator = subscriberEnumerator;
        subscriberEnumerator = null;
        if (enumerator is not null)
        { await ServerFailureObserver.ObserveAsync(() => enumerator.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        var beforeLifetimeDispose = failures.Count;
        try
        { subscriberLifetime?.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        if (failures.Count == beforeLifetimeDispose)
        { subscriberLifetime = null; }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static ContainerResource[] SelectNodeResources(IDistributedApplicationTestingBuilder builder)
    {
        var selected = builder.Resources.OfType<ContainerResource>().Where(resource => IsNode(resource.Name))
            .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        if (selected.Length != RequestCqrsRf3Protocol.NodeCount)
        { throw new InvalidOperationException("The actual Aspire model did not contain three C1 node containers."); }
        return selected;
    }

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;

}

internal static class RequestCqrsRf3DiagnosticsArtifactFiles
{
    private const string DataRootArgument = "--KeyLoad:DataRoot=";
    private const string EphemeralArgument = "--KeyLoad:Ephemeral=true";
    private const string DataRootPrefix = "keyload-c1-diagnostics-";
    private const string ArtifactDirectory = "artifacts";
    private const string QualificationDirectory = "qualification";
    private const string ArtifactPrefix = "request-cqrs-rf3-mcp-rejections-";
    private const string ArtifactSuffix = ".json";
    private const string ArtifactDataKey = "KeyLoad.RequestCqrsRf3.McpRejectionArtifact";
    private const string FailureText = "The diagnostics test requests a bounded artifact.";
    internal const int MaximumArtifactBytes = 16 * 1_024;

    internal static string CreateDataRoot(Guid id)
    {
        var root = Path.Combine(Path.GetTempPath(), DataRootPrefix + id.ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return root;
    }

    internal static string[] CreateAppHostArguments(string dataRoot)
        => [DataRootArgument + dataRoot, EphemeralArgument];

    internal static string ExpectedPath(Guid id)
        => Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, ArtifactDirectory,
            QualificationDirectory, ArtifactPrefix + id.ToString("N") + ArtifactSuffix);

    internal static async Task<byte[]> WriteAndReadAsync(RequestCqrsRf3Diagnostics diagnostics,
        string artifactPath, CancellationToken cancellationToken)
    {
        var failure = new InvalidOperationException(FailureText);
        diagnostics.SaveFailureEvidence(failure);
        var written = failure.Data[ArtifactDataKey] as string;
        if (!string.Equals(written, artifactPath, StringComparison.Ordinal))
        { throw new InvalidOperationException("The diagnostics artifact path did not match the owned wave."); }
        var bytes = await File.ReadAllBytesAsync(artifactPath, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaximumArtifactBytes)
        { throw new InvalidOperationException("The diagnostics artifact exceeded its frozen byte bound."); }
        return bytes;
    }

    internal static void DeleteOwned(string? artifactPath, List<Exception> failures)
    {
        if (artifactPath is not null && File.Exists(artifactPath))
        { ServerFailureObserver.Observe(() => File.Delete(artifactPath), failures); }
    }
}

internal static class RequestCqrsRf3DiagnosticsTestPublisher
{
    private const string MethodToolsCall = "tools/call";
    private const string RawMessageTemplate = "{Line}";
    private const string MessagePrefix = "MCP transport rejected at ";
    private const string Separator = " for ";
    private const string SensitiveSentinel = "private-diagnostic-sentinel-7fe26d";
    private const int MaximumLineCharacters = 4_096;
    private const int ValidRecordsPerNode = 40;

    internal static void EmitMalformed(ILogger logger)
    {
        var valid = FixedMessage(McpTransportStage.ProtocolRevisionValue);
        logger.LogWarning(RawMessageTemplate, valid + new string('X', MaximumLineCharacters + 1) + SensitiveSentinel);
        logger.LogWarning(RawMessageTemplate, valid + " " + MessagePrefix + "ProtocolRevisionValue for ToolsCall. " + SensitiveSentinel);
        logger.LogWarning(RawMessageTemplate, MessagePrefix + "1" + Separator + "2.");
        logger.LogWarning(RawMessageTemplate, MessagePrefix + SensitiveSentinel + Separator + "ToolsCall.");
        logger.LogWarning(RawMessageTemplate, MessagePrefix + "ProtocolRevisionValue" + Separator + SensitiveSentinel + ".");
        logger.LogWarning(RawMessageTemplate, valid + " trailing " + SensitiveSentinel);
    }

    internal static void EmitValid(ILogger logger)
        => EmitRejectedTransport(logger, McpTransportStage.ProtocolRevisionValue);

    internal static void EmitFortyValid(ILogger logger)
    {
        for (var index = 0; index < ValidRecordsPerNode; index++)
        { EmitValid(logger); }
    }

    private static void EmitRejectedTransport(ILogger logger, McpTransportStage stage)
    {
        var error = Errors.Fail(ErrorCode.Validation, McpTransportProtocol.InvalidTransport);
        error.Data[McpTransportDiagnostics.StageMetadataKey] = stage;
        var headers = new HeaderDictionary { [McpTransportProtocol.MethodHeader] = MethodToolsCall };
        McpTransportDiagnostics.Log(logger, error, headers);
    }

    private static string FixedMessage(McpTransportStage stage)
        => MessagePrefix + stage + Separator + nameof(McpTransportMethodCategory.ToolsCall) + ".";
}

internal static class RequestCqrsRf3DiagnosticsArtifactAssertions
{
    private const string SensitiveSentinel = "private-diagnostic-sentinel-7fe26d";
    private const int MaximumArtifactBytes = RequestCqrsRf3DiagnosticsArtifactFiles.MaximumArtifactBytes;

    internal static async Task AssertAsync(byte[] bytes, Guid waveId, int perNode)
    {
        await Assert.That(bytes.Length).IsLessThanOrEqualTo(MaximumArtifactBytes);
        var text = Encoding.UTF8.GetString(bytes);
        await Assert.That(text.Contains(SensitiveSentinel, StringComparison.Ordinal)).IsFalse();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        await AssertPropertiesAsync(root, "Rejections", "Version", "WaveId").ConfigureAwait(false);
        await Assert.That(root.GetProperty("Version").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("WaveId").GetGuid()).IsEqualTo(waveId);
        await Assert.That(waveId != Guid.Empty).IsTrue();
        await AssertRecordsAsync(root.GetProperty("Rejections"), waveId, perNode).ConfigureAwait(false);
    }

    private static async Task AssertRecordsAsync(JsonElement values, Guid waveId, int perNode)
    {
        var records = values.EnumerateArray().ToArray();
        await Assert.That(records.Length).IsEqualTo(RequestCqrsRf3Protocol.NodeCount * perNode);
        foreach (var record in records)
        {
            await AssertPropertiesAsync(record, "MethodCategory", "Node", "Stage", "WaveId").ConfigureAwait(false);
            await Assert.That(record.GetProperty("WaveId").GetGuid()).IsEqualTo(waveId);
            await Assert.That(record.GetProperty("Stage").GetInt32()).IsEqualTo((int)McpTransportStage.ProtocolRevisionValue);
            await Assert.That(record.GetProperty("MethodCategory").GetInt32())
                .IsEqualTo((int)McpTransportMethodCategory.ToolsCall);
        }
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2,
                     RequestCqrsRf3Protocol.Node3 })
        {
            await Assert.That(records.Count(record => string.Equals(record.GetProperty("Node").GetString(), node, StringComparison.Ordinal)))
                .IsEqualTo(perNode);
        }
    }

    private static async Task AssertPropertiesAsync(JsonElement value, params string[] expected)
    {
        var actual = value.EnumerateObject().Select(property => property.Name)
            .Order(StringComparer.Ordinal).ToArray();
        await Assert.That(actual.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
    }
}

internal static class RequestCqrsRf3DiagnosticsSuccessEvidence
{
    internal static async Task AssertUnavailableBeforeJoinAsync(RequestCqrsRf3DiagnosticsTestScope scope)
    {
        var rejected = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => SaveEvidenceAsync(scope.Capture)).ConfigureAwait(false);
        if (rejected is null || File.Exists(scope.OwnedArtifactPath))
        {
            throw new InvalidOperationException("Unjoined diagnostics unexpectedly produced success evidence.");
        }
    }

    internal static async Task<(string Path, byte[] Bytes)> CompleteAndReadMemoizedAsync(
        RequestCqrsRf3DiagnosticsTestScope scope, CancellationToken cancellationToken)
    {
        await scope.JoinOriginalSubscriptionsAsync().ConfigureAwait(false);
        var capture = scope.Capture;
        var firstPath = capture.SaveEvidence();
        var firstBytes = await File.ReadAllBytesAsync(firstPath, cancellationToken).ConfigureAwait(false);
        var secondPath = capture.SaveEvidence();
        var secondBytes = await File.ReadAllBytesAsync(secondPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(firstPath, scope.OwnedArtifactPath, StringComparison.Ordinal)
            || !string.Equals(secondPath, firstPath, StringComparison.Ordinal)
            || !firstBytes.AsSpan().SequenceEqual(secondBytes))
        {
            throw new InvalidOperationException("Repeated success evidence changed its path or bytes.");
        }
        return (firstPath, firstBytes);
    }

    private static async Task SaveEvidenceAsync(RequestCqrsRf3Diagnostics capture)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        _ = capture.SaveEvidence();
    }
}
