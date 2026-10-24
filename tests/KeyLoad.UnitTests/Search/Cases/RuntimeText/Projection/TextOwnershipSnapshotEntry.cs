namespace KeyLoad.UnitTests.Search;

internal sealed record TextOwnershipSnapshotEntry(string RelativePath, bool IsDirectory, string? Sha256);
