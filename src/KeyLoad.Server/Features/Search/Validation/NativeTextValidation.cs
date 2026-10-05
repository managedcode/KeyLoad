using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextValidation
{
    private const string GenerationIdentityFormat = "N";
    private const string ParentDirectorySegment = "..";

    internal static void ValidateManifest(NativeTextManifest manifest, TextProjectionScope scope, int maximumRecords)
    {
        if (manifest is null || manifest.FormatVersion != NativeTextProtocol.FormatVersion || manifest.Scope != scope
            || manifest.TokenizerVersion != TextProjectionProtocol.TokenizerVersion
            || manifest.HashVersion != TextProjectionProtocol.HashVersion || manifest.Records is null
            || manifest.Files is null || manifest.Files.Length is 0 or > NativeTextProtocol.MaximumFiles
            || manifest.Records.Length > maximumRecords)
        {
            throw NativeTextErrors.Corrupt();
        }
        var references = new HashSet<EntityRef>();
        for (var index = 0; index < manifest.Records.Length; index++)
        {
            ValidateRecord(manifest.Records[index], scope, index, references);
        }
        ValidateFiles(manifest.Files);
    }

    internal static void ValidateScope(TextProjectionScope scope, Guid sourceNodeId)
    {
        if (scope is null || scope.NodeId != sourceNodeId || scope.Incarnation == Guid.Empty || scope.DataEpoch <= 0
            || scope.ReadGeneration < 0 || scope.Position < 0 || scope.PolicyEpoch < 0 || scope.SchemaVersion < 0
            || scope.Partition is null || string.IsNullOrWhiteSpace(scope.Collection)
            || string.IsNullOrWhiteSpace(scope.Field) || string.IsNullOrWhiteSpace(scope.PrincipalId))
        {
            throw NativeTextErrors.Corrupt();
        }
    }

    internal static bool IsGenerationLeaf(string leaf)
        => leaf.StartsWith(NativeTextProtocol.GenerationPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(leaf[NativeTextProtocol.GenerationPrefix.Length..], "N", out var id)
            && string.Equals(leaf, NativeTextProtocol.GenerationPrefix + id.ToString(GenerationIdentityFormat), StringComparison.Ordinal);

    internal static string GenerationLeaf() => NativeTextProtocol.GenerationPrefix + Guid.NewGuid().ToString(GenerationIdentityFormat);

    internal static void ValidateOwnedPaths(NativeTextOwnedPath[] paths)
    {
        if (paths is null || paths.Length is 0 or > NativeTextProtocol.MaximumEntries)
        {
            throw NativeTextErrors.Corrupt();
        }
        string? previous = null;
        var files = 0;
        var directories = 0;
        foreach (var path in paths)
        {
            if (path is null || string.IsNullOrWhiteSpace(path.RelativePath)
                || path.RelativePath.StartsWith('/')
                || path.RelativePath.Contains(ParentDirectorySegment, StringComparison.Ordinal)
                || path.RelativePath.Contains('\\', StringComparison.Ordinal)
                || previous is not null && StringComparer.Ordinal.Compare(previous, path.RelativePath) >= 0)
            {
                throw NativeTextErrors.Corrupt();
            }
            var depth = path.RelativePath.Count(character => character == '/') + 1;
            if (depth > NativeTextProtocol.MaximumDepth
                || (path.IsDirectory ? ++directories > NativeTextProtocol.MaximumDirectories
                    : ++files > NativeTextProtocol.MaximumFiles))
            {
                throw NativeTextErrors.BoundExceeded();
            }
            previous = path.RelativePath;
        }
        if (paths[0] != new NativeTextOwnedPath(NativeTextProtocol.NativeDirectory, true))
        {
            throw NativeTextErrors.Corrupt();
        }
    }

    private static void ValidateRecord(NativeTextRecord? record, TextProjectionScope scope, int index,
        HashSet<EntityRef> references)
    {
        if (record is null || record.Id != (ulong)index + 1 || record.Revision < 0 || record.Reference is null
            || record.Reference.Partition != scope.Partition || record.Reference.Collection != scope.Collection
            || string.IsNullOrEmpty(record.Reference.Id) || !references.Add(record.Reference))
        {
            throw NativeTextErrors.Corrupt();
        }
    }

    private static void ValidateFiles(NativeTextFile[] files)
    {
        long bytes = 0;
        string? previous = null;
        foreach (var file in files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.RelativePath) || file.RelativePath.StartsWith('/')
                || file.RelativePath.Contains(ParentDirectorySegment, StringComparison.Ordinal)
                || file.RelativePath.Contains('\\', StringComparison.Ordinal) || file.Length < 0
                || file.Sha256 is null || file.Sha256.Length != System.Security.Cryptography.SHA256.HashSizeInBytes
                || previous is not null && StringComparer.Ordinal.Compare(previous, file.RelativePath) >= 0
                || file.Length > NativeTextProtocol.MaximumDiskBytes - bytes)
            {
                throw NativeTextErrors.Corrupt();
            }
            bytes += file.Length;
            previous = file.RelativePath;
        }
    }
}

internal static class NativeTextErrors
{
    internal static KeyLoadException Corrupt() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.Corruption,
        NativeTextProtocol.ProjectionCorrupt);

    internal static KeyLoadException Ownership() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.FormatUnsupported,
        NativeTextProtocol.ProjectionOwnership);

    internal static KeyLoadException BoundExceeded() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.BudgetExceeded,
        NativeTextProtocol.ProjectionBoundExceeded);

    internal static KeyLoadException Mismatch() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.HistoryUnavailable,
        NativeTextProtocol.ProjectionMismatch);

    internal static KeyLoadException Busy() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.BudgetExceeded,
        NativeTextProtocol.ProjectionBusy);
}
