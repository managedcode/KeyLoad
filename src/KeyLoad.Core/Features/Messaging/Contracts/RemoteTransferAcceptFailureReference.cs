namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAcceptFailureReferenceFields
{
    internal const uint Generation = 0;
    internal const uint AcceptCommandId = 1;
    internal const uint OutcomeDigest = 2;
    internal const uint WitnessToken = 3;
    internal const string Alias = "keyload.core.queue-transfer.accept-failure-reference.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferAcceptFailureReferenceFields.Alias)]
internal sealed record RemoteTransferAcceptFailureReference(
    [property: global::Orleans.Id(RemoteTransferAcceptFailureReferenceFields.Generation)] long Generation,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureReferenceFields.AcceptCommandId)] Guid AcceptCommandId,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureReferenceFields.OutcomeDigest)] string OutcomeDigest,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureReferenceFields.WitnessToken)] string WitnessToken);
