namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferPeerProtocol
{
    internal const string CallAlias = "keyload.queue-transfer.peer-call.v1";
    internal const string ProofAlias = "keyload.core.queue-transfer.native-proof.v1";
    internal const string ResultAlias = "keyload.queue-transfer.peer-result.v1";
    internal const string AuthorityAlias = "keyload.core.queue-transfer.outcome-authority.v1";
    internal const string RemoteTargetAlias = "keyload.core.queue-transfer.remote-target.v1";
    internal const string RemoteOriginAlias = "keyload.core.queue-transfer.remote-origin.v1";
    internal const string ExternalReceiptAlias = "keyload.queue-transfer.external-receipt.v1";
    internal const string ExternalReceiptPurpose = "keyload-queue-transfer-external-receipt-v1";
    internal const string GrainPurpose = "keyload-grain-queue-transfer-v1";
    internal const string Unavailable = "The native remote queue transfer is unavailable.";
    internal const string Invalid = "The native remote queue transfer proof is invalid.";
    internal const string Unknown = "The remote write outcome is unknown. Reconcile the same command ID.";
    internal const int ReplyField = 10;
    internal const int SelectedVoter = 0;
    internal const int SignatureCount = 1;
    internal const int Version = 1;
    internal const int SingleMutation = 1;
    internal const int FirstMutation = 0;
    internal const int AcceptStage = 0;
    internal const int ReceiptStage = 1;
    internal const int OutcomeStage = 2;
    internal const long MinimumPolicyEpoch = 1;
    internal const int EmptyEncodedBytes = 0;
    internal const long MaximumPosition = long.MaxValue;
    internal const string OriginalAuthorizationFailure = "keyload.queue-transfer.original-authorization-failure";
}
