namespace KeyLoad.ProcessRecoveryTests.DocumentStorage;

internal sealed record ScalarIndexMembership(string PartitionKey, string Value, string[] DocumentIds);
