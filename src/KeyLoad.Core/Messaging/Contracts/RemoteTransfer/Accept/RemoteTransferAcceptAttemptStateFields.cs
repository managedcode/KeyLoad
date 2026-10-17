namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAcceptAttemptStateFields
{
    internal const uint Ceiling = 0;
    internal const uint Generation = 1;
    internal const uint History = 2;
    internal const string Alias = "keyload.core.queue-transfer.accept-attempt-state.v1";
}
