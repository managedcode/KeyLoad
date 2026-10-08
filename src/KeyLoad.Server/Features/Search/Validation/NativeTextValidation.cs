using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextValidation
{
    private const string IsGenerationLeafCompactIdentityFormat = "N";

    private const string GenerationIdentityFormat = "N";
    private const string ParentDirectorySegment = "..";

    internal static void ValidateManifest(NativeTextManifest manifest, TextProjectionScope scope, int maximumRecords, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const int EmptyFilesLength = 0;
        const int IndexInitialValue = 0;

        if (manifest is null || manifest.FormatVersion != NativeTextProtocol.FormatVersion || manifest.Scope != scope
            || manifest.TokenizerVersion != TextProjectionProtocol.TokenizerVersion
            || manifest.HashVersion != TextProjectionProtocol.HashVersion || manifest.Records is null
            || manifest.Files is null || (manifest.Files.Length == EmptyFilesLength || manifest.Files.Length > executionOptions.Value.MaximumFiles)
            || manifest.Records.Length > maximumRecords)
        {
            throw NativeTextErrors.Corrupt();
        }
        var references = new HashSet<EntityRef>();
        for (var index = IndexInitialValue; index < manifest.Records.Length; index++)
        {
            ValidateRecord(manifest.Records[index], scope, index, references);
        }
        ValidateFiles(manifest.Files, executionOptions: executionOptions);
    }

    internal static void ValidateScope(TextProjectionScope scope, Guid sourceNodeId)
    {
        const int DataEpochValidationBoundary = 0;
        const int ReadGenerationValidationBoundary = 0;
        const int PositionValidationBoundary = 0;
        const int PolicyEpochValidationBoundary = 0;
        const int SchemaVersionValidationBoundary = 0;

        if (scope is null || scope.NodeId != sourceNodeId || scope.Incarnation == Guid.Empty || scope.DataEpoch <= DataEpochValidationBoundary
            || scope.ReadGeneration < ReadGenerationValidationBoundary || scope.Position < PositionValidationBoundary || scope.PolicyEpoch < PolicyEpochValidationBoundary || scope.SchemaVersion < SchemaVersionValidationBoundary
            || scope.Partition is null || string.IsNullOrWhiteSpace(scope.Collection)
            || string.IsNullOrWhiteSpace(scope.Field) || string.IsNullOrWhiteSpace(scope.PrincipalId))
        {
            throw NativeTextErrors.Corrupt();
        }
    }

    internal static bool IsGenerationLeaf(string leaf)
        => leaf.StartsWith(NativeTextProtocol.GenerationPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(leaf[NativeTextProtocol.GenerationPrefix.Length..], IsGenerationLeafCompactIdentityFormat, out var id)
            && string.Equals(leaf, NativeTextProtocol.GenerationPrefix + id.ToString(GenerationIdentityFormat), StringComparison.Ordinal);

    internal static string GenerationLeaf() => NativeTextProtocol.GenerationPrefix + Guid.NewGuid().ToString(GenerationIdentityFormat);

    internal static void ValidateOwnedPaths(NativeTextOwnedPath[] paths, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const int EmptyPathsLength = 0;
        const int FilesInitialValue = 0;
        const int DirectoriesInitialValue = 0;
        const char SlashCharacter = '/';
        const char BackslashCharacter = '\\';
        const int CompareValidationBoundary = 0;
        const int RelativePathCountStep = 1;
        const int PathsFirstIndex = 0;

        if (paths is null || (paths.Length == EmptyPathsLength || paths.Length > executionOptions.Value.MaximumEntries))
        {
            throw NativeTextErrors.Corrupt();
        }
        string? previous = null;
        var files = FilesInitialValue;
        var directories = DirectoriesInitialValue;
        foreach (var path in paths)
        {
            if (path is null || string.IsNullOrWhiteSpace(path.RelativePath)
                || path.RelativePath.StartsWith(SlashCharacter)
                || path.RelativePath.Contains(ParentDirectorySegment, StringComparison.Ordinal)
                || path.RelativePath.Contains(BackslashCharacter, StringComparison.Ordinal)
                || previous is not null && StringComparer.Ordinal.Compare(previous, path.RelativePath) >= CompareValidationBoundary)
            {
                throw NativeTextErrors.Corrupt();
            }
            var depth = path.RelativePath.Count(character => character == SlashCharacter) + RelativePathCountStep;
            if (depth > executionOptions.Value.MaximumDepth
                || (path.IsDirectory ? ++directories > executionOptions.Value.MaximumDirectories
                    : ++files > executionOptions.Value.MaximumFiles))
            {
                throw NativeTextErrors.BoundExceeded();
            }
            previous = path.RelativePath;
        }
        if (paths[PathsFirstIndex] != new NativeTextOwnedPath(NativeTextProtocol.NativeDirectory, true))
        {
            throw NativeTextErrors.Corrupt();
        }
    }

    private static void ValidateRecord(NativeTextRecord? record, TextProjectionScope scope, int index,
        HashSet<EntityRef> references)
    {
        const int IndexStep = 1;
        const int RevisionValidationBoundary = 0;

        if (record is null || record.Id != (ulong)index + IndexStep || record.Revision < RevisionValidationBoundary || record.Reference is null
            || record.Reference.Partition != scope.Partition || record.Reference.Collection != scope.Collection
            || string.IsNullOrEmpty(record.Reference.Id) || !references.Add(record.Reference))
        {
            throw NativeTextErrors.Corrupt();
        }
    }

    internal static void ValidateFiles(NativeTextFile[] files, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const int BytesInitialValue = 0;
        const char SlashCharacter = '/';
        const char BackslashCharacter = '\\';
        const int FileLengthValidationBoundary = 0;
        const int CompareValidationBoundary = 0;

        long bytes = BytesInitialValue;
        string? previous = null;
        foreach (var file in files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.RelativePath) || file.RelativePath.StartsWith(SlashCharacter)
                || file.RelativePath.Contains(ParentDirectorySegment, StringComparison.Ordinal)
                || file.RelativePath.Contains(BackslashCharacter, StringComparison.Ordinal) || file.Length < FileLengthValidationBoundary
                || file.Sha256 is null || file.Sha256.Length != System.Security.Cryptography.SHA256.HashSizeInBytes
                || previous is not null && StringComparer.Ordinal.Compare(previous, file.RelativePath) >= CompareValidationBoundary
                || file.Length > executionOptions.Value.MaximumDiskBytes - bytes)
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
