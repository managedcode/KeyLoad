namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochRf3FileEntry(string Path, long Length, string Sha256, int Mode);
