namespace KeyLoad.RecoveryHost.Features.DocumentStorage;

internal sealed record ScalarIndexMembership(string PartitionKey, string Value, string[] DocumentIds);
