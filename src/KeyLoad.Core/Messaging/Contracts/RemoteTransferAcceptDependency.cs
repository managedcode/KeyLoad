namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAcceptDependencyFields
{
    internal const uint PrincipalPolicyEpoch = 0;
    internal const uint FieldHeaderPolicyDigest = 1;
    internal const uint QueueMaxStoredMessages = 2;
    internal const uint QueueMaxStoredBytes = 3;
    internal const uint QueueStoredMessages = 4;
    internal const uint QueueStoredBytes = 5;
    internal const uint TransferStoredRecords = 6;
    internal const uint TransferStoredBytes = 7;
    internal const uint MaxBatchBytes = 8;
    internal const uint MaxScanRecords = 9;
    internal const string Alias = "keyload.core.queue-transfer.accept-dependency.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferAcceptDependencyFields.Alias)]
internal sealed record RemoteTransferAcceptDependency(
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.PrincipalPolicyEpoch)] long PrincipalPolicyEpoch,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.FieldHeaderPolicyDigest)] string FieldHeaderPolicyDigest,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.QueueMaxStoredMessages)] long QueueMaxStoredMessages,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.QueueMaxStoredBytes)] long QueueMaxStoredBytes,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.QueueStoredMessages)] long QueueStoredMessages,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.QueueStoredBytes)] long QueueStoredBytes,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.TransferStoredRecords)] long TransferStoredRecords,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.TransferStoredBytes)] long TransferStoredBytes,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.MaxBatchBytes)] int MaxBatchBytes,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.MaxScanRecords)] int MaxScanRecords);
