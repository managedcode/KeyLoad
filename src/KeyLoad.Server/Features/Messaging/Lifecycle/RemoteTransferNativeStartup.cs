using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Messaging;

internal static class RemoteTransferNativeStartup
{
    internal static Task Start(RemoteTransferPeerVerificationOwner? verification,
        NativeRequestWorkOwner requests, Func<Task, CancellationToken, Task> start,
        CancellationToken cancellationToken)
    {
        verification?.AttachRequests(requests);
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startup = start(registered.Task, cancellationToken);
        registered.SetResult();
        return startup;
    }
}
