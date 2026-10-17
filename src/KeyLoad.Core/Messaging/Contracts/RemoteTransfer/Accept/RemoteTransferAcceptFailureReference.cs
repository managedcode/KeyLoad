namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferAcceptFailureReferenceFields.Alias)]
internal sealed record RemoteTransferAcceptFailureReference(
    [property: global::Orleans.Id(RemoteTransferAcceptFailureReferenceFields.Generation)] long Generation,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureReferenceFields.AcceptCommandId)] Guid AcceptCommandId,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureReferenceFields.OutcomeDigest)] string OutcomeDigest,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureReferenceFields.WitnessToken)] string WitnessToken);
