namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeWireTrial
{
    internal static Task RunAsync(CancellationToken token)
        => RemoteTransferNativeReplayTrial.RunAsync(
            KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeReplayVariation.AllWireFaults6, token);
}
