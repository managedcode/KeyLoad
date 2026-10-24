namespace KeyLoad.RecoveryTests.DocumentStorage;

internal sealed record CompositeIndexMembership(string PartitionKey, string Value, string[] DocumentIds);
