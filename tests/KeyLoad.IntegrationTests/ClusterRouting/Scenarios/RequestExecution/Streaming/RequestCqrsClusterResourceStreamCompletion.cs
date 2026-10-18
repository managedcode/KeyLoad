using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal static class RequestCqrsClusterResourceStreamCompletion
{
    internal static void Complete(RequestCqrsResourceCompletionEvidence evidence, ResourceLoggerService logger,
        ProjectResource[] resources, Func<string, TaskStatus?> statusFor, List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? failureObserver)
    {
        foreach (var resource in resources)
        {
            evidence.CompleteResourceStream(logger, resource, statusFor, failures, failureObserver,
                CompletionStage(resource.Name));
        }
    }

    private static RequestCqrsLifecycleStage CompletionStage(string resourceName) => resourceName switch
    {
        RequestCqrsClusterProtocol.Node1 => RequestCqrsLifecycleStage.CaptureCompleteNode1,
        RequestCqrsClusterProtocol.Node2 => RequestCqrsLifecycleStage.CaptureCompleteNode2,
        RequestCqrsClusterProtocol.Node3 => RequestCqrsLifecycleStage.CaptureCompleteNode3,
        _ => throw new ArgumentOutOfRangeException(nameof(resourceName))
    };
}
