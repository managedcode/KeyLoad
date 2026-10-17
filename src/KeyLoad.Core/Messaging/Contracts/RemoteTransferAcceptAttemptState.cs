namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAcceptAttemptStateFields
{
    internal const uint Ceiling = 0;
    internal const uint Generation = 1;
    internal const uint History = 2;
    internal const string Alias = "keyload.core.queue-transfer.accept-attempt-state.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferAcceptAttemptStateFields.Alias)]
internal sealed record RemoteTransferAcceptAttemptState(
    [property: global::Orleans.Id(RemoteTransferAcceptAttemptStateFields.Ceiling)] int Ceiling,
    [property: global::Orleans.Id(RemoteTransferAcceptAttemptStateFields.Generation)] long Generation,
    [property: global::Orleans.Id(RemoteTransferAcceptAttemptStateFields.History)] System.Collections.Immutable.ImmutableArray<RemoteTransferAcceptFailureReference> History);
