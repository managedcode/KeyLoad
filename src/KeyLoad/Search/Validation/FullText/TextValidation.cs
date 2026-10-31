using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextValidation
{
    private const string IsGenerationLeafCompactIdentityFormat = "N";

    private const string GenerationIdentityFormat = "N";
    private const string ParentDirectorySegment = "..";

    internal static void ValidateManifest(TextManifest manifest, TextProjectionScope scope, IOptions<TextExecutionOptions> executionOptions)
    {
        const int EmptyFilesLength = 0;
        const int IndexInitialValue = 0;

        if (manifest is null || manifest.FormatVersion != TextProtocol.FormatVersion || manifest.Scope != scope
            || manifest.TokenizerVersion != TextProjectionProtocol.TokenizerVersion
            || manifest.HashVersion != TextProjectionProtocol.HashVersion || manifest.Records is null
            || manifest.Files is null || (manifest.Files.Length == EmptyFilesLength || manifest.Files.Length > executionOptions.Value.MaximumFiles))
        {
            throw TextErrors.Corrupt();
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
            throw TextErrors.Corrupt();
        }
    }

    internal static bool IsGenerationLeaf(string leaf)
        => leaf.StartsWith(TextProtocol.GenerationPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(leaf[TextProtocol.GenerationPrefix.Length..], IsGenerationLeafCompactIdentityFormat, out var id)
            && string.Equals(leaf, TextProtocol.GenerationPrefix + id.ToString(GenerationIdentityFormat), StringComparison.Ordinal);

    internal static string GenerationLeaf() => TextProtocol.GenerationPrefix + Guid.NewGuid().ToString(GenerationIdentityFormat);

    internal static void ValidateOwnedPaths(TextOwnedPath[] paths, IOptions<TextExecutionOptions> executionOptions)
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
            throw TextErrors.Corrupt();
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
                throw TextErrors.Corrupt();
            }
            var depth = path.RelativePath.Count(character => character == SlashCharacter) + RelativePathCountStep;
            if (depth > executionOptions.Value.MaximumDepth
                || (path.IsDirectory ? ++directories > executionOptions.Value.MaximumDirectories
                    : ++files > executionOptions.Value.MaximumFiles))
            {
                throw TextErrors.BoundExceeded();
            }
            previous = path.RelativePath;
        }
        if (paths[PathsFirstIndex] != new TextOwnedPath(TextProtocol.RuntimeDirectoryCore, true))
        {
            throw TextErrors.Corrupt();
        }
    }

    private static void ValidateRecord(TextRecord? record, TextProjectionScope scope, int index,
        HashSet<EntityRef> references)
    {
        const int IndexStep = 1;
        const int RevisionValidationBoundary = 0;

        if (record is null || record.Id != (ulong)index + IndexStep || record.Revision < RevisionValidationBoundary || record.Reference is null
            || record.Reference.Partition != scope.Partition || record.Reference.Collection != scope.Collection
            || string.IsNullOrEmpty(record.Reference.Id) || !references.Add(record.Reference))
        {
            throw TextErrors.Corrupt();
        }
    }

    internal static void ValidateFiles(TextFile[] files, IOptions<TextExecutionOptions> executionOptions)
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
                || previous is not null && StringComparer.Ordinal.Compare(previous, file.RelativePath) >= CompareValidationBoundary
                || file.Length > executionOptions.Value.MaximumDiskBytes - bytes)
            {
                throw TextErrors.Corrupt();
            }
            bytes += file.Length;
            previous = file.RelativePath;
        }
    }
}
