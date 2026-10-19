namespace KeyLoad.UnitTests.Features.Search;

internal sealed record TextOwnershipSnapshotEntry(string RelativePath, bool IsDirectory, string? Sha256);
