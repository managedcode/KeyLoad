using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3FixtureArtifactValidation
{
    internal static NativeCoverageRf3MaterializerReceipt ReadMaterializer(byte[] bytes, string evidenceRoot,
        string receiptPath)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (!HasExactProperties(root, NativeCoverageRf3FixtureProtocol.ContextDirectoryProperty,
                NativeCoverageRf3FixtureProtocol.ManifestPathProperty,
                NativeCoverageRf3FixtureProtocol.ManifestSha256Property,
                NativeCoverageRf3FixtureProtocol.FileCountProperty,
                NativeCoverageRf3FixtureProtocol.TotalBytesProperty))
        {
            throw Invalid();
        }
        var directory = ResolveUnder(RequiredString(root, NativeCoverageRf3FixtureProtocol.ContextDirectoryProperty),
            evidenceRoot);
        var manifestPath = ResolveUnder(RequiredString(root, NativeCoverageRf3FixtureProtocol.ManifestPathProperty),
            directory);
        var expectedManifest = Path.GetFullPath(Path.Combine(directory,
            NativeCoverageRf3FixtureProtocol.ContextManifestHashFilePath));
        var count = root.GetProperty(NativeCoverageRf3FixtureProtocol.FileCountProperty).GetInt32();
        var bytesTotal = root.GetProperty(NativeCoverageRf3FixtureProtocol.TotalBytesProperty).GetInt64();
        if (Path.GetFullPath(receiptPath) == manifestPath || manifestPath != expectedManifest
            || count <= 0 || bytesTotal <= 0)
        {
            throw Invalid();
        }
        var hash = RequiredString(root, NativeCoverageRf3FixtureProtocol.ManifestSha256Property);
        if (!IsSha256(hash))
        {
            throw Invalid();
        }

        return new(directory, manifestPath, hash, count, bytesTotal);
    }

    internal static NativeCoverageRf3BaseImageReceipt ReadBaseReceipt(byte[] bytes, string sourceRevision,
        string contextBaseReference)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (!HasExactProperties(root, NativeCoverageRf3FixtureProtocol.SchemaVersionProperty,
                NativeCoverageRf3FixtureProtocol.SourceRevisionProperty,
                NativeCoverageRf3FixtureProtocol.SourcePathProperty,
                NativeCoverageRf3FixtureProtocol.SourceSha256Property,
                NativeCoverageRf3FixtureProtocol.ImageReferenceProperty))
        {
            throw Invalid();
        }
        var reference = RequiredString(root, NativeCoverageRf3FixtureProtocol.ImageReferenceProperty);
        var sha = RequiredString(root, NativeCoverageRf3FixtureProtocol.SourceSha256Property);
        if (root.GetProperty(NativeCoverageRf3FixtureProtocol.SchemaVersionProperty).GetInt32()
                != NativeCoverageRf3Protocol.BaseImageSchemaVersion
            || RequiredString(root, NativeCoverageRf3FixtureProtocol.SourceRevisionProperty) != sourceRevision
            || RequiredString(root, NativeCoverageRf3FixtureProtocol.SourcePathProperty)
                != NativeCoverageRf3FixtureProtocol.DockerfileName
            || reference != contextBaseReference || !IsPinnedBaseReference(reference) || !IsSha256(sha))
        {
            throw Invalid();
        }

        return new(reference, sha);
    }

    internal static void ValidatePreparation(NativeCoverageRf3MaterializerReceipt materializer,
        NativeCoverageRf3PreparedContext context,
        NativeCoverageRf3BaseImageReceipt baseReceipt, byte[] baseReceiptBytes, string contextHash, string expectedTemplateHash,
        string evidenceRoot, string materializerPath)
    {
        var expectedManifest = Path.Combine(materializer.Directory,
            NativeCoverageRf3FixtureProtocol.ContextManifestHashFilePath);
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

    internal static bool BoundsMatch(JsonElement actual, NativeCoverageRf3ExecutionBounds expected)
        => actual.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumFilesProperty).GetInt32()
                == expected.MaximumFiles
        && actual.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumTotalBytesProperty).GetInt64()
            == expected.MaximumTotalBytes
        && actual.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumFileBytesProperty).GetInt64()
            == expected.MaximumFileBytes
        && actual.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumPathCharactersProperty).GetInt32()
            == expected.MaximumPathCharacters
        && actual.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumManifestBytesProperty).GetInt64()
            == expected.MaximumManifestBytes
        && actual.GetProperty(NativeCoverageRf3FixtureProtocol.ReadBufferBytesProperty).GetInt32()
            == expected.ReadBufferBytes
        && actual.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumReportBytesProperty).GetInt64()
            == expected.MaximumReportBytes
        && actual.GetProperty(NativeCoverageRf3FixtureProtocol.ShutdownSecondsProperty).GetInt32()
            == Seconds(expected.ShutdownTimeout)
        && actual.GetProperty(NativeCoverageRf3FixtureProtocol.SettlementSecondsProperty).GetInt32()
            == Seconds(expected.SettlementTimeout);

    internal static string ReadImageId(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var value = document.RootElement.GetString();
        return value is { Length: 71 } && value.StartsWith(NativeCoverageRf3FixtureProtocol.Sha256Prefix,
                StringComparison.Ordinal)
            && value.AsSpan(NativeCoverageRf3FixtureProtocol.Sha256Prefix.Length)
                .IndexOfAnyExcept(NativeCoverageRf3FixtureProtocol.ShaCharacters) < 0 ? value : throw Invalid();
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
        => !Path.IsPathRooted(relative) && relative != NativeCoverageRf3FixtureProtocol.CurrentDirectory
            && relative != NativeCoverageRf3FixtureProtocol.ParentDirectory
            && !relative.StartsWith(NativeCoverageRf3FixtureProtocol.ParentDirectoryPrefix, StringComparison.Ordinal);

    internal static bool IsSha256(string value) => value.Length == NativeCoverageRf3Protocol.JsonShaHexLength
        && value.AsSpan().IndexOfAnyExcept(NativeCoverageRf3FixtureProtocol.ShaCharacters) < 0;

    private static bool IsPinnedBaseReference(string value)
    {
        var marker = value.LastIndexOf(NativeCoverageRf3FixtureProtocol.ImageDigestSeparator, StringComparison.Ordinal);
        return value.StartsWith(NativeCoverageRf3FixtureProtocol.AspNetBaseImagePrefix, StringComparison.Ordinal)
            && marker > NativeCoverageRf3FixtureProtocol.AspNetBaseImagePrefix.Length
            && value.Length - marker - NativeCoverageRf3FixtureProtocol.ImageDigestSeparator.Length
                == NativeCoverageRf3Protocol.JsonShaHexLength
            && IsSha256(value[(marker + NativeCoverageRf3FixtureProtocol.ImageDigestSeparator.Length)..]);
    }

    internal static int Seconds(string value) => checked((int)TimeSpan.ParseExact(value,
        NativeCoverageRf3RunProtocol.TimeSpanFormat, CultureInfo.InvariantCulture).TotalSeconds);

    internal static InvalidOperationException Invalid() =>
        new(NativeCoverageRf3FixtureProtocol.InvalidContext);
}
