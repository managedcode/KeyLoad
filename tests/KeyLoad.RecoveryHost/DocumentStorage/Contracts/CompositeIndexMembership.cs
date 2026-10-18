namespace KeyLoad.RecoveryHost.Features.DocumentStorage;

internal sealed record CompositeIndexMembership(string PartitionKey, string Value, string[] DocumentIds);
