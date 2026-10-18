namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal sealed record ScalarIndexMembership(string PartitionKey, string Value, string[] DocumentIds);
