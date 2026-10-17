namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal sealed record ScalarIndexMembership(string PartitionKey, string Value, string[] DocumentIds);
