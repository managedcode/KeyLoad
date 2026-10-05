using Aspire.Hosting.ApplicationModel;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

/// <summary>Owns native stream completion and the memoized join for one diagnostics test scope.</summary>
internal sealed class RequestCqrsRf3DiagnosticsScopeStreamOwner(ResourceLoggerService logger,
    ContainerResource[] resources, RequestCqrsLifecycleEvidence lifecycle)
{
    private readonly HashSet<string> completedResourceNames = new(StringComparer.Ordinal);
    private RequestCqrsRf3Diagnostics? diagnostics;

    internal void BindDiagnostics(RequestCqrsRf3Diagnostics value) => diagnostics = value;

    internal void CompleteResource(string node)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.CaptureJoin);
        var resource = resources.Single(candidate => string.Equals(candidate.Name, node, StringComparison.Ordinal));
        var failures = new List<Exception>();
        RequestCqrsLifecycleFailureObserver.Observe(() => logger.Complete(resource), failures,
            lifecycle.RecordOwnerFailure, CompletionStage(resource.Name));
        ServerFailureObserver.ThrowIfAny(failures);
        completedResourceNames.Add(resource.Name);
    }

    internal void CompleteResourceStreams()
    {
        var failures = new List<Exception>();
        CompleteResourceStreams(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void CompleteResourceStreams(List<Exception> failures)
    {
        foreach (var resource in resources)
        {
            if (completedResourceNames.Contains(resource.Name))
            { continue; }
            var before = failures.Count;
            RequestCqrsLifecycleFailureObserver.Observe(() => logger.Complete(resource), failures,
                lifecycle.RecordOwnerFailure, CompletionStage(resource.Name));
            if (failures.Count == before)
            { completedResourceNames.Add(resource.Name); }
        }
    }

    private static RequestCqrsLifecycleStage CompletionStage(string resourceName) => resourceName switch
    {
        RequestCqrsRf3Protocol.Node1 => RequestCqrsLifecycleStage.CaptureCompleteNode1,
        RequestCqrsRf3Protocol.Node2 => RequestCqrsLifecycleStage.CaptureCompleteNode2,
        RequestCqrsRf3Protocol.Node3 => RequestCqrsLifecycleStage.CaptureCompleteNode3,
        _ => throw new ArgumentOutOfRangeException(nameof(resourceName))
    };

    internal async Task JoinDiagnosticsTwiceAsync()
    {
        var capture = diagnostics ?? throw new InvalidOperationException("The Aspire diagnostics owner is absent.");
        var first = capture.DisposeAsync().AsTask();
        var repeated = capture.DisposeAsync().AsTask();
        if (!ReferenceEquals(first, repeated))
        { throw new InvalidOperationException("Repeated diagnostics disposal did not share one completion task."); }
        await first.ConfigureAwait(false);
    }
}
