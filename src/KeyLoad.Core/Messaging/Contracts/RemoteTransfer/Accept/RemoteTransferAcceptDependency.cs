namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferAcceptDependencyFields.Alias)]
internal sealed record RemoteTransferAcceptDependency(
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.PrincipalPolicyEpoch)] long PrincipalPolicyEpoch,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.FieldHeaderPolicyDigest)] string FieldHeaderPolicyDigest,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.QueueMaxStoredMessages)] long QueueMaxStoredMessages,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.QueueMaxStoredBytes)] long QueueMaxStoredBytes,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.QueueStoredMessages)] long QueueStoredMessages,
    [property: global::Orleans.Id(RemoteTransferAcceptDependencyFields.QueueStoredBytes)] long QueueStoredBytes);
