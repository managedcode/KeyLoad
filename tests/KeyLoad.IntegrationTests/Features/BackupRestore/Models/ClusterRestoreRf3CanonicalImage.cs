namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal sealed record ClusterRestoreRf3CanonicalImage(PartitionRef Partition, long Count, string Digest);
