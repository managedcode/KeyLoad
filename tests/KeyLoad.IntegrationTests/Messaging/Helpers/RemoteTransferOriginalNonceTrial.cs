namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferOriginalNonceTrial
{
    internal static Task RunAsync(CancellationToken token)
        => RemoteTransferNativeReplayTrial.RunAsync(
            KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeReplayVariation.Original2, token);
}
