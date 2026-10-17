namespace KeyLoad.UnitTests.Features.Search;

internal sealed record NativeTextOwnershipSnapshotEntry(string RelativePath, bool IsDirectory, string? Sha256);
