namespace KeyLoad.RecoveryTests.DocumentStorage;

internal sealed record ScalarIndexMembership(string PartitionKey, string Value, string[] DocumentIds);
