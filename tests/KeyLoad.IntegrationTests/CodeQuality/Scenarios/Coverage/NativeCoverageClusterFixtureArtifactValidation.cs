using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterFixtureArtifactValidation
{
    internal static NativeCoverageClusterMaterializerReceipt ReadMaterializer(byte[] bytes, string evidenceRoot,
        string receiptPath)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (!HasExactProperties(root, NativeCoverageClusterFixtureProtocol.ContextDirectoryProperty,
                NativeCoverageClusterFixtureProtocol.ManifestPathProperty,
                NativeCoverageClusterFixtureProtocol.ManifestSha256Property,
                NativeCoverageClusterFixtureProtocol.FileCountProperty,
                NativeCoverageClusterFixtureProtocol.TotalBytesProperty))
        {
            throw Invalid();
        }
        var directory = ResolveUnder(RequiredString(root, NativeCoverageClusterFixtureProtocol.ContextDirectoryProperty),
            evidenceRoot);
        var manifestPath = ResolveUnder(RequiredString(root, NativeCoverageClusterFixtureProtocol.ManifestPathProperty),
            directory);
        var expectedManifest = Path.GetFullPath(Path.Combine(directory,
            NativeCoverageClusterFixtureProtocol.ContextManifestHashFilePath));
        var count = root.GetProperty(NativeCoverageClusterFixtureProtocol.FileCountProperty).GetInt32();
        var bytesTotal = root.GetProperty(NativeCoverageClusterFixtureProtocol.TotalBytesProperty).GetInt64();
        if (Path.GetFullPath(receiptPath) == manifestPath || manifestPath != expectedManifest
            || count <= 0 || bytesTotal <= 0)
        {
            throw Invalid();
        }
        var hash = RequiredString(root, NativeCoverageClusterFixtureProtocol.ManifestSha256Property);
        if (!IsSha256(hash))
        {
            throw Invalid();
        }

        return new(directory, manifestPath, hash, count, bytesTotal);
    }

    internal static NativeCoverageClusterBaseImageReceipt ReadBaseReceipt(byte[] bytes, string sourceRevision,
        string contextBaseReference)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (!HasExactProperties(root, NativeCoverageClusterFixtureProtocol.SchemaVersionProperty,
                NativeCoverageClusterFixtureProtocol.SourceRevisionProperty,
                NativeCoverageClusterFixtureProtocol.SourcePathProperty,
                NativeCoverageClusterFixtureProtocol.SourceSha256Property,
                NativeCoverageClusterFixtureProtocol.ImageReferenceProperty))
        {
            throw Invalid();
        }
        var reference = RequiredString(root, NativeCoverageClusterFixtureProtocol.ImageReferenceProperty);
        var sha = RequiredString(root, NativeCoverageClusterFixtureProtocol.SourceSha256Property);
        if (root.GetProperty(NativeCoverageClusterFixtureProtocol.SchemaVersionProperty).GetInt32()
                != NativeCoverageClusterProtocol.BaseImageSchemaVersion
            || RequiredString(root, NativeCoverageClusterFixtureProtocol.SourceRevisionProperty) != sourceRevision
            || RequiredString(root, NativeCoverageClusterFixtureProtocol.SourcePathProperty)
                != NativeCoverageClusterFixtureProtocol.ContainerSettingsPath
            || reference != contextBaseReference || !IsPinnedBaseReference(reference) || !IsSha256(sha))
        {
            throw Invalid();
        }

        return new(reference, sha);
    }

    internal static void ValidatePreparation(NativeCoverageClusterMaterializerReceipt materializer,
        NativeCoverageClusterPreparedContext context,
        NativeCoverageClusterBaseImageReceipt baseReceipt, byte[] baseReceiptBytes, string contextHash, string expectedTemplateHash,
        string evidenceRoot, string materializerPath)
    {
        var expectedManifest = Path.Combine(materializer.Directory,
            NativeCoverageClusterFixtureProtocol.ContextManifestHashFilePath);
        var relative = Path.GetRelativePath(evidenceRoot, materializer.Directory);
        if (Path.GetFullPath(materializer.ManifestPath) != Path.GetFullPath(expectedManifest)
            || !IsContained(relative) || materializer.ManifestHash != contextHash
            || materializer.FileCount != checked(context.FileCount + 1)
            || materializer.TotalBytes != checked(context.TotalBytes + context.ManifestLength)
            || Path.GetFullPath(materializerPath) == Path.GetFullPath(materializer.ManifestPath)
            || context.BaseImageReference != baseReceipt.ImageReference
            || context.BaseImageReceiptSha256 != Hash(baseReceiptBytes)
            || context.SourceTemplateDockerfileSha256 != expectedTemplateHash)
        {
            throw Invalid();
        }
    }

    internal static bool BoundsMatch(JsonElement actual, NativeCoverageClusterExecutionBounds expected)
        => actual.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumFilesProperty).GetInt32()
                == expected.MaximumFiles
        && actual.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumTotalBytesProperty).GetInt64()
            == expected.MaximumTotalBytes
        && actual.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumFileBytesProperty).GetInt64()
            == expected.MaximumFileBytes
        && actual.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumPathCharactersProperty).GetInt32()
            == expected.MaximumPathCharacters
        && actual.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumManifestBytesProperty).GetInt64()
            == expected.MaximumManifestBytes
        && actual.GetProperty(NativeCoverageClusterFixtureProtocol.ReadBufferBytesProperty).GetInt32()
            == expected.ReadBufferBytes
        && actual.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumReportBytesProperty).GetInt64()
            == expected.MaximumReportBytes
        && actual.GetProperty(NativeCoverageClusterFixtureProtocol.ShutdownSecondsProperty).GetInt32()
            == Seconds(expected.ShutdownTimeout)
        && actual.GetProperty(NativeCoverageClusterFixtureProtocol.SettlementSecondsProperty).GetInt32()
            == Seconds(expected.SettlementTimeout);

    internal static string ReadImageId(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var value = document.RootElement.GetString();
        return value is { Length: 71 } && value.StartsWith(NativeCoverageClusterFixtureProtocol.Sha256Prefix,
                StringComparison.Ordinal)
            && value.AsSpan(NativeCoverageClusterFixtureProtocol.Sha256Prefix.Length)
                .IndexOfAnyExcept(NativeCoverageClusterFixtureProtocol.ShaCharacters) < 0 ? value : throw Invalid();
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static string RequiredString(JsonElement element, string property)
    {
        var value = element.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value) ? throw Invalid() : value;
    }

    internal static bool HasExactProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != names.Length)
        {
            return false;
        }
        var expected = new HashSet<string>(names, StringComparer.Ordinal);
        return element.EnumerateObject().All(property => expected.Remove(property.Name)) && expected.Count == 0;
    }

    internal static string ResolveUnder(string path, string root)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw Invalid();
        }

        var resolved = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Path.GetFullPath(root), resolved);
        if (!IsContained(relative))
        {
            throw Invalid();
        }
        return resolved;
    }

    private static bool IsContained(string relative)
        => !Path.IsPathRooted(relative) && relative != NativeCoverageClusterFixtureProtocol.CurrentDirectory
            && relative != NativeCoverageClusterFixtureProtocol.ParentDirectory
            && !relative.StartsWith(NativeCoverageClusterFixtureProtocol.ParentDirectoryPrefix, StringComparison.Ordinal);

    internal static bool IsSha256(string value) => value.Length == NativeCoverageClusterProtocol.JsonShaHexLength
        && value.AsSpan().IndexOfAnyExcept(NativeCoverageClusterFixtureProtocol.ShaCharacters) < 0;

    private static bool IsPinnedBaseReference(string value)
    {
        var marker = value.LastIndexOf(NativeCoverageClusterFixtureProtocol.ImageDigestSeparator, StringComparison.Ordinal);
        return value.StartsWith(NativeCoverageClusterFixtureProtocol.AspNetBaseImagePrefix, StringComparison.Ordinal)
            && marker > NativeCoverageClusterFixtureProtocol.AspNetBaseImagePrefix.Length
            && value.Length - marker - NativeCoverageClusterFixtureProtocol.ImageDigestSeparator.Length
                == NativeCoverageClusterProtocol.JsonShaHexLength
            && IsSha256(value[(marker + NativeCoverageClusterFixtureProtocol.ImageDigestSeparator.Length)..]);
    }

    internal static int Seconds(string value) => checked((int)TimeSpan.ParseExact(value,
        NativeCoverageClusterRunProtocol.TimeSpanFormat, CultureInfo.InvariantCulture).TotalSeconds);

    internal static InvalidOperationException Invalid() =>
        new(NativeCoverageClusterFixtureProtocol.InvalidContext);
}
