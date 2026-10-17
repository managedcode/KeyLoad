namespace KeyLoad.Core.Features.Messaging;

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
