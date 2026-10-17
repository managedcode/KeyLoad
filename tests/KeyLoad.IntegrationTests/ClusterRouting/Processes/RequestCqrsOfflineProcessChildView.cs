using System.Diagnostics;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsOfflineProcessChildView(RequestCqrsOfflineProcessOwnedChild owner)
{
    internal Task CancelAsync() => owner.CancelAsync();
    internal Task ReleaseOutputAsync() => owner.ReleaseOutputAsync();
    internal Task<Process> WaitForStartedAsync() => owner.WaitForStartedAsync();
    internal Task<OperationCanceledException> ExpectCancellationAsync() => owner.ExpectCancellationAsync();
    internal Task<InvalidOperationException> ExpectOutputLimitAsync() => owner.ExpectOutputLimitAsync();
}
