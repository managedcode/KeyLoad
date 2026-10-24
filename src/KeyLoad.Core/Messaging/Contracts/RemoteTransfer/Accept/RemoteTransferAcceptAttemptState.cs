namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferAcceptAttemptStateFields.Alias)]
internal sealed record RemoteTransferAcceptAttemptState(
    [property: global::Orleans.Id(RemoteTransferAcceptAttemptStateFields.Ceiling)] int Ceiling,
    [property: global::Orleans.Id(RemoteTransferAcceptAttemptStateFields.Generation)] long Generation,
    [property: global::Orleans.Id(RemoteTransferAcceptAttemptStateFields.History)] System.Collections.Immutable.ImmutableArray<RemoteTransferAcceptFailureReference> History);
