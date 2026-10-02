using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageMetadata(string BrowserVersion, IReadOnlySet<string> Origins,
    IReadOnlyList<string> CoverageFiles);

internal static class SiteCoverageArtifactMetadata
{
    private static readonly IReadOnlySet<string> NodeEnvelopeRequired = new HashSet<string>([SiteCoverageTokens.Result], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> NodeEnvelopeOptional = new HashSet<string>([SiteCoverageTokens.Timestamp, SiteCoverageTokens.SourceMapCache], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> BrowserEnvelopeRequired = new HashSet<string>([SiteCoverageTokens.Result], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> BrowserEnvelopeOptional = new HashSet<string>([SiteCoverageTokens.Timestamp], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> MetadataRequired = Set(SiteCoverageTokens.SchemaVersion,
        SiteCoverageTokens.SourceRevision, SiteCoverageTokens.BrowserVersion, SiteCoverageTokens.Origins,
        SiteCoverageTokens.Sources, SiteCoverageTokens.CoverageFiles);
    private static readonly IReadOnlySet<string> SourceRequired = Set(SiteCoverageTokens.Path, SiteCoverageTokens.Sha256);

    public static SiteCoverageMetadata ParseMetadata(byte[] bytes, SiteCoverageSourceManifest manifest,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        using var document = JsonDocument.Parse(bytes, SiteCoverageTokens.JsonDocumentOptions);
        var root = document.RootElement;
        SiteCoverageNativeJson.RequireObject(root, MetadataRequired, EmptySet.Value);
        if (ReadInt32(root, SiteCoverageTokens.SchemaVersion) != SiteCoverageTokens.Schema ||
            SiteCoverageNativeJson.GetString(root, SiteCoverageTokens.SourceRevision) != manifest.SourceRevision)
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidMetadataFailure);
        }

        var browserVersion = SiteCoverageNativeJson.GetString(root, SiteCoverageTokens.BrowserVersion);
        if (browserVersion.Length == SiteCoverageTokens.Zero)
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidMetadataFailure);
        }

        var origins = ReadOrigins(root.GetProperty(SiteCoverageTokens.Origins));
        ValidateMetadataSources(root.GetProperty(SiteCoverageTokens.Sources), sources);
        var coverageFiles = ReadCoverageFileNames(root.GetProperty(SiteCoverageTokens.CoverageFiles));
        return new(browserVersion, origins, coverageFiles);
    }

    public static HashSet<string> ReadOrigins(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == SiteCoverageTokens.Zero)
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidMetadataFailure);
        }

        var origins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } text || !IsLoopbackOrigin(text) ||
                !origins.Add(text))
            {
                throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidMetadataFailure);
            }
        }

        return origins;
    }

    public static void ValidateMetadataSources(JsonElement value,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> expected)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != expected.Count)
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidMetadataFailure);
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in value.EnumerateArray())
        {
            SiteCoverageNativeJson.RequireObject(source, SourceRequired, EmptySet.Value);
            var path = SiteCoverageNativeJson.GetString(source, SiteCoverageTokens.Path);
            var hash = SiteCoverageNativeJson.GetString(source, SiteCoverageTokens.Sha256);
            if (!expected.TryGetValue(path, out var entry) || entry.Sha256 != hash || !actual.Add(path))
            {
                throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidMetadataFailure);
            }
        }
    }

    public static string[] ReadCoverageFileNames(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == SiteCoverageTokens.Zero)
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidMetadataFailure);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } name ||
                Path.GetFileName(name) != name || Path.GetExtension(name) != SiteCoverageTokens.JsonExtension ||
                name.Contains(SiteCoverageTokens.RelativeSeparator, StringComparison.Ordinal) ||
                name.Contains(SiteCoverageTokens.ParentSegmentMarker, StringComparison.Ordinal) ||
                name.Contains(SiteCoverageTokens.Backslash, StringComparison.Ordinal) || !names.Add(name))
            {
                throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.ExtensionFailure);
            }
        }

        return names.Order(StringComparer.Ordinal).ToArray();
    }

    public static void ValidateSessionFiles(string directory, IReadOnlySet<string> listedNames)
    {
        var actualFiles = Directory.EnumerateFileSystemEntries(directory, SiteCoverageTokens.Wildcard,
                SearchOption.TopDirectoryOnly).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        if (!actualFiles.SetEquals(listedNames.Append(SiteCoverageTokens.MetadataFile)))
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidMetadataFailure);
        }
    }

    internal static void ValidateEnvelope(JsonElement value, string runtime)
    {
        var required = runtime == SiteCoverageTokens.NodeRuntime ? NodeEnvelopeRequired : BrowserEnvelopeRequired;
        var optional = runtime == SiteCoverageTokens.NodeRuntime ? NodeEnvelopeOptional : BrowserEnvelopeOptional;
        SiteCoverageNativeJson.RequireObject(value, required, optional);
        if (value.TryGetProperty(SiteCoverageTokens.Timestamp, out var timestamp) &&
            (timestamp.ValueKind != JsonValueKind.Number || !timestamp.TryGetDouble(out var time) || !double.IsFinite(time)))
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.JsonFailure);
        }

        if (runtime == SiteCoverageTokens.NodeRuntime && value.TryGetProperty(SiteCoverageTokens.SourceMapCache, out var cache) &&
            (cache.ValueKind != JsonValueKind.Object || cache.EnumerateObject().Any()))
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.SourceMapUnsupportedFailure);
        }
    }

    private static bool IsLoopbackOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == SiteCoverageTokens.HttpScheme && uri.Host == SiteCoverageTokens.LoopbackAddress &&
        uri.Port > SiteCoverageTokens.Zero && uri.AbsolutePath == SiteCoverageTokens.Slash &&
        uri.UserInfo.Length == SiteCoverageTokens.Zero && uri.Query.Length == SiteCoverageTokens.Zero &&
        uri.Fragment.Length == SiteCoverageTokens.Zero;

    private static int ReadInt32(JsonElement value, string property)
    {
        var item = value.GetProperty(property);
        return item.TryGetInt32(out var number) ? number : throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.JsonFailure);
    }

    private static HashSet<string> Set(params string[] fields) => new(fields, StringComparer.Ordinal);
    private static class EmptySet { public static readonly IReadOnlySet<string> Value = new HashSet<string>(StringComparer.Ordinal); }
}
