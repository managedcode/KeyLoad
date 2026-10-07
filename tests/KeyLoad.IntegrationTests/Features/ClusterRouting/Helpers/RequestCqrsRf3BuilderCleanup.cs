using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3BuilderCleanup
{
    internal static async Task<bool> DisposeAsync(IDistributedApplicationTestingBuilder? builder,
        List<Exception> failures, Action<RequestCqrsLifecycleStage>? observer)
    {
        if (builder is null)
        { return true; }
        var previous = failures.Count;
        await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => builder.DisposeAsync().AsTask(), failures,
            observer, RequestCqrsLifecycleStage.AuthorityWaveBuilderDispose).ConfigureAwait(false);
        return failures.Count == previous;
    }
}
