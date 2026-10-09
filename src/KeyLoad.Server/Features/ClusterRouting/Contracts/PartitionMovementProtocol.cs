namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementProtocol
{
    internal const int NoReadBytes = 0;
    internal const int NoExaminedRecords = 0;
    internal const int NoResultBytes = 0;
    internal const int NoAppliedPosition = 0;
    internal const int UnissuedPolicyEpoch = 0;
    internal const int InitialPhaseOrdinal = 0;
    internal const int InitialPlacementRevision = 0;
    internal const int InitialParentGeneration = 0;
    internal const long InitialCleanupGeneration = 0;
    internal const string NoOriginalPhaseIdentityDigest = "";
    internal const int InitialCleanupFamily = 0;
    internal const int InitialCleanupBatch = 0;
    internal const int NoFailures = 0;
    internal const int SingleFailure = 1;
    internal const int FirstFailureIndex = 0;
    internal const string ReceiverIssueQueryAlias = "keyload.server.partition-move-receiver-issue-query.v1";
    internal const string ReceiverIssueRequestAlias = "keyload.server.partition-move-receiver-issue-request.v1";
    internal const string SourcePendingReplyAlias = "keyload.server.partition-move-source-pending-reply.v1";
    internal const string TransferAuthorityReplyAlias = "keyload.server.partition-move-transfer-authority-reply.v1";
    internal const string TransferDataReplyAlias = "keyload.server.partition-move-transfer-data-reply.v1";
    internal const string TransferDataRequestAlias = "keyload.server.partition-move-transfer-data-request.v1";
    internal const string ReceiverIssueNonceAlias = "keyload.server.partition-move-receiver-issue-nonce.v1";
    internal const string ParentPhaseIdentityAlias = "keyload.server.partition-move-parent-phase-identity.v1";
    internal const string ParentPhaseRoleAlias = "keyload.server.partition-move-parent-phase-role.v1";
    internal const string RetireCancellationRequestAlias = "keyload.server.partition-move-retire-cancellation-request.v1";
    internal const string RetireCancellationQueryAlias = "keyload.server.partition-move-retire-cancellation-query.v1";
    internal const string OutcomeReadTransportFailed = "The original partition outcome READ transport send failed.";

    internal const string ActionAlias = "keyload.server.v1.PartitionMovementTransportAction";
    internal const string ReplyAlias = "keyload.server.v1.PartitionMovementTransportReply";
    internal const string RequestAlias = "keyload.server.v1.PartitionMovementTransportRequest";
    internal const string OutcomeRequestAlias = "keyload.server.v1.PartitionMovementOutcomeTransportRequest";
    internal const string ReceiverIssuePath = "/internal/partitions/movement/v1/receiver-issuance";
    internal const string ReceiverIssueProofPath = "/internal/partitions/movement/v1/receiver-issuance-proof";
    internal const string TransferDataPath = "/internal/partitions/movement/v1/transfer-data";
    internal const string OutcomePath = "/internal/partitions/movement/v1/outcomes";
    internal const string Path = "/internal/partitions/movement/v1";
    internal const string ContentType = "application/octet-stream";
    internal const string SignatureHeader = "X-KeyLoad-Movement-Proof";
    internal const string InvalidProof = "The partition movement peer proof is invalid.";
    internal const string Unavailable = "The partition movement receiver is unavailable.";
}
