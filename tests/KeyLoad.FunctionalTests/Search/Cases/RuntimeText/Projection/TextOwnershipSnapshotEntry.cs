namespace KeyLoad.FunctionalTests.Search;

internal sealed record TextOwnershipSnapshotEntry(string RelativePath, bool IsDirectory, string? Sha256);
