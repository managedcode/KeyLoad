using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterContextInventoryReader
{
    internal static void ValidateProducerSources(JsonElement sources, NativeCoverageClusterExecutionBounds bounds)
    {
        if (!NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(sources,
                NativeCoverageClusterFixtureProtocol.ContractsProducerSource,
                NativeCoverageClusterFixtureProtocol.FilesProducerSource,
                NativeCoverageClusterFixtureProtocol.ToolProducerSource,
                NativeCoverageClusterFixtureProtocol.MaterializerProducerSource,
                NativeCoverageClusterFixtureProtocol.EntryProducerSource))
        {
            throw Invalid();
        }
        foreach (var source in sources.EnumerateObject())
        {
            var metadata = source.Value;
            if (!NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(metadata,
                    NativeCoverageClusterFixtureProtocol.ModeProperty,
                    NativeCoverageClusterFixtureProtocol.LengthProperty,
                    NativeCoverageClusterFixtureProtocol.Sha256Property))
            {
                throw Invalid();
            }
            var length = metadata.GetProperty(NativeCoverageClusterFixtureProtocol.LengthProperty).GetInt64();
            if (length <= 0 || length > bounds.MaximumManifestBytes
                || !NativeCoverageClusterFixtureArtifactValidation.IsSha256(
                    NativeCoverageClusterFixtureArtifactValidation.RequiredString(metadata,
                        NativeCoverageClusterFixtureProtocol.Sha256Property))
                || metadata.GetProperty(NativeCoverageClusterFixtureProtocol.ModeProperty).ValueKind
                    != JsonValueKind.Number)
            {
                throw Invalid();
            }
        }
    }

    internal static (string Hash, int Count, long TotalBytes) ReadDockerfileHash(JsonElement files,
        NativeCoverageClusterExecutionBounds bounds)
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
            if (!NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(file,
                    NativeCoverageClusterFixtureProtocol.PathProperty,
                    NativeCoverageClusterFixtureProtocol.ModeProperty,
                    NativeCoverageClusterFixtureProtocol.LengthProperty,
                    NativeCoverageClusterFixtureProtocol.Sha256Property))
            {
                throw Invalid();
            }
            var path = NativeCoverageClusterFixtureArtifactValidation.RequiredString(file,
                NativeCoverageClusterFixtureProtocol.PathProperty);
            var length = file.GetProperty(NativeCoverageClusterFixtureProtocol.LengthProperty).GetInt64();
            var hash = NativeCoverageClusterFixtureArtifactValidation.RequiredString(file,
                NativeCoverageClusterFixtureProtocol.Sha256Property);
            if (!seen.Add(path) || !IsRelativePath(path, bounds.MaximumPathCharacters)
                || length <= 0 || length > bounds.MaximumFileBytes
                || !NativeCoverageClusterFixtureArtifactValidation.IsSha256(hash)
                || file.GetProperty(NativeCoverageClusterFixtureProtocol.ModeProperty).ValueKind != JsonValueKind.Number)
            {
                throw Invalid();
            }
            total = checked(total + length);
            if (path == NativeCoverageClusterFixtureProtocol.DockerfileName)
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
        if (!NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(sourceTemplates,
                NativeCoverageClusterFixtureProtocol.DockerfileSha256Property,
                NativeCoverageClusterFixtureProtocol.DockerfileModeProperty,
                NativeCoverageClusterFixtureProtocol.DockerignoreSha256Property,
                NativeCoverageClusterFixtureProtocol.DockerignoreModeProperty,
                NativeCoverageClusterFixtureProtocol.SettingsSha256Property,
                NativeCoverageClusterFixtureProtocol.WrapperSha256Property,
                NativeCoverageClusterFixtureProtocol.WrapperModeProperty,
                NativeCoverageClusterFixtureProtocol.LifecycleSha256Property,
                NativeCoverageClusterFixtureProtocol.LifecycleModeProperty,
                NativeCoverageClusterFixtureProtocol.TargetSha256Property,
                NativeCoverageClusterFixtureProtocol.TargetModeProperty)
            || !NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(baseImage,
                NativeCoverageClusterFixtureProtocol.ReferenceProperty,
                NativeCoverageClusterFixtureProtocol.SourceReceiptSha256Property))
        {
            throw Invalid();
        }
        var hashes = new[]
        {
            NativeCoverageClusterFixtureProtocol.DockerfileSha256Property,
            NativeCoverageClusterFixtureProtocol.DockerignoreSha256Property,
            NativeCoverageClusterFixtureProtocol.SettingsSha256Property,
            NativeCoverageClusterFixtureProtocol.WrapperSha256Property,
            NativeCoverageClusterFixtureProtocol.LifecycleSha256Property,
            NativeCoverageClusterFixtureProtocol.TargetSha256Property
        };
        var modes = new[]
        {
            NativeCoverageClusterFixtureProtocol.DockerfileModeProperty,
            NativeCoverageClusterFixtureProtocol.DockerignoreModeProperty,
            NativeCoverageClusterFixtureProtocol.WrapperModeProperty,
            NativeCoverageClusterFixtureProtocol.LifecycleModeProperty,
            NativeCoverageClusterFixtureProtocol.TargetModeProperty
        };
        if (hashes.Any(name => !NativeCoverageClusterFixtureArtifactValidation.IsSha256(
                NativeCoverageClusterFixtureArtifactValidation.RequiredString(sourceTemplates, name)))
            || modes.Any(name => sourceTemplates.GetProperty(name).ValueKind != JsonValueKind.Number)
            || !NativeCoverageClusterFixtureArtifactValidation.IsSha256(
                NativeCoverageClusterFixtureArtifactValidation.RequiredString(baseImage,
                    NativeCoverageClusterFixtureProtocol.SourceReceiptSha256Property)))
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

        return path.Split(NativeCoverageClusterFixtureProtocol.PathSeparator)
            .All(part => part.Length > 0 && part != NativeCoverageClusterFixtureProtocol.ParentDirectory
                && part != NativeCoverageClusterFixtureProtocol.CurrentDirectory);
    }

    private static InvalidOperationException Invalid() =>
        NativeCoverageClusterFixtureArtifactValidation.Invalid();
}
