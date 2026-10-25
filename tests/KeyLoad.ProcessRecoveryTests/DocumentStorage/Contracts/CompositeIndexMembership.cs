namespace KeyLoad.ProcessRecoveryTests.DocumentStorage;

internal sealed record CompositeIndexMembership(string PartitionKey, string Value, string[] DocumentIds);
