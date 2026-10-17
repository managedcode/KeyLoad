using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3ContextInventoryReader
{
    internal static void ValidateProducerSources(JsonElement sources, NativeCoverageRf3ExecutionBounds bounds)
    {
        if (!NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(sources,
                NativeCoverageRf3FixtureProtocol.ContractsProducerSource,
                NativeCoverageRf3FixtureProtocol.FilesProducerSource,
                NativeCoverageRf3FixtureProtocol.ToolProducerSource,
                NativeCoverageRf3FixtureProtocol.MaterializerProducerSource,
                NativeCoverageRf3FixtureProtocol.EntryProducerSource))
        {
            throw Invalid();
        }
        foreach (var source in sources.EnumerateObject())
        {
            var metadata = source.Value;
            if (!NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(metadata,
                    NativeCoverageRf3FixtureProtocol.ModeProperty,
                    NativeCoverageRf3FixtureProtocol.LengthProperty,
                    NativeCoverageRf3FixtureProtocol.Sha256Property))
            {
                throw Invalid();
            }
            var length = metadata.GetProperty(NativeCoverageRf3FixtureProtocol.LengthProperty).GetInt64();
            if (length <= 0 || length > bounds.MaximumManifestBytes
                || !NativeCoverageRf3FixtureArtifactValidation.IsSha256(
                    NativeCoverageRf3FixtureArtifactValidation.RequiredString(metadata,
                        NativeCoverageRf3FixtureProtocol.Sha256Property))
                || metadata.GetProperty(NativeCoverageRf3FixtureProtocol.ModeProperty).ValueKind
                    != JsonValueKind.Number)
            {
                throw Invalid();
            }
        }
    }

    internal static (string Hash, int Count, long TotalBytes) ReadDockerfileHash(JsonElement files,
        NativeCoverageRf3ExecutionBounds bounds)
    {
        if (files.ValueKind != JsonValueKind.Array)
        {
            throw Invalid();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? dockerfileHash = null;
        long total = 0;
        var count = 0;
        foreach (var file in files.EnumerateArray())
        {
            count++;
            if (!NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(file,
                    NativeCoverageRf3FixtureProtocol.PathProperty,
                    NativeCoverageRf3FixtureProtocol.ModeProperty,
                    NativeCoverageRf3FixtureProtocol.LengthProperty,
                    NativeCoverageRf3FixtureProtocol.Sha256Property))
            {
                throw Invalid();
            }
            var path = NativeCoverageRf3FixtureArtifactValidation.RequiredString(file,
                NativeCoverageRf3FixtureProtocol.PathProperty);
            var length = file.GetProperty(NativeCoverageRf3FixtureProtocol.LengthProperty).GetInt64();
            var hash = NativeCoverageRf3FixtureArtifactValidation.RequiredString(file,
                NativeCoverageRf3FixtureProtocol.Sha256Property);
            if (!seen.Add(path) || !IsRelativePath(path, bounds.MaximumPathCharacters)
                || length <= 0 || length > bounds.MaximumFileBytes
                || !NativeCoverageRf3FixtureArtifactValidation.IsSha256(hash)
                || file.GetProperty(NativeCoverageRf3FixtureProtocol.ModeProperty).ValueKind != JsonValueKind.Number)
            {
                throw Invalid();
            }
            total = checked(total + length);
            if (path == NativeCoverageRf3FixtureProtocol.DockerfileName)
            {
                dockerfileHash = hash;
            }
        }
        if (count == 0 || count > bounds.MaximumFiles || total <= 0
            || total > bounds.MaximumTotalBytes || dockerfileHash is null)
        {
            throw Invalid();
        }

        return (dockerfileHash, count, total);
    }

    internal static void ValidateManifestSections(JsonElement sourceTemplates, JsonElement baseImage)
    {
        if (!NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(sourceTemplates,
                NativeCoverageRf3FixtureProtocol.DockerfileSha256Property,
                NativeCoverageRf3FixtureProtocol.DockerfileModeProperty,
                NativeCoverageRf3FixtureProtocol.DockerignoreSha256Property,
                NativeCoverageRf3FixtureProtocol.DockerignoreModeProperty,
                NativeCoverageRf3FixtureProtocol.SettingsSha256Property,
                NativeCoverageRf3FixtureProtocol.WrapperSha256Property,
                NativeCoverageRf3FixtureProtocol.WrapperModeProperty,
                NativeCoverageRf3FixtureProtocol.LifecycleSha256Property,
                NativeCoverageRf3FixtureProtocol.LifecycleModeProperty,
                NativeCoverageRf3FixtureProtocol.TargetSha256Property,
                NativeCoverageRf3FixtureProtocol.TargetModeProperty)
            || !NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(baseImage,
                NativeCoverageRf3FixtureProtocol.ReferenceProperty,
                NativeCoverageRf3FixtureProtocol.SourceReceiptSha256Property))
        {
            throw Invalid();
        }
        var hashes = new[]
        {
            NativeCoverageRf3FixtureProtocol.DockerfileSha256Property,
            NativeCoverageRf3FixtureProtocol.DockerignoreSha256Property,
            NativeCoverageRf3FixtureProtocol.SettingsSha256Property,
            NativeCoverageRf3FixtureProtocol.WrapperSha256Property,
            NativeCoverageRf3FixtureProtocol.LifecycleSha256Property,
            NativeCoverageRf3FixtureProtocol.TargetSha256Property
        };
        var modes = new[]
        {
            NativeCoverageRf3FixtureProtocol.DockerfileModeProperty,
            NativeCoverageRf3FixtureProtocol.DockerignoreModeProperty,
            NativeCoverageRf3FixtureProtocol.WrapperModeProperty,
            NativeCoverageRf3FixtureProtocol.LifecycleModeProperty,
            NativeCoverageRf3FixtureProtocol.TargetModeProperty
        };
        if (hashes.Any(name => !NativeCoverageRf3FixtureArtifactValidation.IsSha256(
                NativeCoverageRf3FixtureArtifactValidation.RequiredString(sourceTemplates, name)))
            || modes.Any(name => sourceTemplates.GetProperty(name).ValueKind != JsonValueKind.Number)
            || !NativeCoverageRf3FixtureArtifactValidation.IsSha256(
                NativeCoverageRf3FixtureArtifactValidation.RequiredString(baseImage,
                    NativeCoverageRf3FixtureProtocol.SourceReceiptSha256Property)))
        {
            throw Invalid();
        }
    }

    private static bool IsRelativePath(string path, int maximumCharacters)
    {
        if (path.Length > maximumCharacters || path.Contains('\\', StringComparison.Ordinal)
            || Path.IsPathRooted(path))
        {
            return false;
        }

        return path.Split(NativeCoverageRf3FixtureProtocol.PathSeparator)
            .All(part => part.Length > 0 && part != NativeCoverageRf3FixtureProtocol.ParentDirectory
                && part != NativeCoverageRf3FixtureProtocol.CurrentDirectory);
    }

    private static InvalidOperationException Invalid() =>
        NativeCoverageRf3FixtureArtifactValidation.Invalid();
}
