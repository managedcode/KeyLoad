namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferPostAwaitTrial
{
    internal static Task RunAsync(CancellationToken token)
        => RemoteTransferNativePostAwaitTrial.RunAsync(token);
}
