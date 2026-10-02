using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteCoverageNativeSourceResolver
{
    public static string? ResolveScriptSource(JsonElement script, string runtime, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources, IReadOnlySet<string>? browserOrigins)
    {
        SiteCoverageNativeJson.RequireObject(script, SiteCoverageNativeJson.NativeFields.ScriptRequired,
            SiteCoverageNativeJson.NativeFields.ScriptOptional);
        _ = SiteCoverageNativeJson.GetString(script, SiteCoverageTokens.ScriptId);
        var url = SiteCoverageNativeJson.GetString(script, SiteCoverageTokens.Url);
        if (url.Length == SiteCoverageTokens.Zero)
        {
            return null;
        }

        return runtime == SiteCoverageTokens.NodeRuntime
            ? ResolveNodeSource(url, repository, sources)
            : ResolveBrowserSource(url, browserOrigins!, sources);
    }

    private static string? ResolveNodeSource(string url, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != SiteCoverageTokens.FileScheme ||
            uri.Query.Length != SiteCoverageTokens.Zero || uri.Fragment.Length != SiteCoverageTokens.Zero ||
            uri.UserInfo.Length != SiteCoverageTokens.Zero)
        {
            return null;
        }

        var fullPath = Path.GetFullPath(uri.LocalPath);
        var matched = sources.Keys.FirstOrDefault(path => SiteCoverageSourcePaths.PathsEqual(
            Path.Combine(repository, path.Replace(SiteCoverageTokens.RelativeSeparator, Path.DirectorySeparatorChar)), fullPath));
        if (matched is not null)
        {
            return matched;
        }

        var siteRoot = Path.GetFullPath(Path.Combine(repository, SiteCoverageTokens.SiteSourcePrefix));
        var portablePath = fullPath.Replace(Path.DirectorySeparatorChar, SiteCoverageTokens.RelativeSeparator);
        if (IsWithin(fullPath, siteRoot) && !portablePath.Contains(SiteCoverageTokens.VendorSourcePrefix,
            StringComparison.Ordinal))
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.UnexpectedProductionScriptFailure);
        }

        return null;
    }

    private static string? ResolveBrowserSource(string url, IReadOnlySet<string> browserOrigins,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != SiteCoverageTokens.HttpScheme)
        {
            return null;
        }

        var path = uri.AbsolutePath;
        if (!path.StartsWith(SiteCoverageTokens.FeatureUrlPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (!browserOrigins.Contains(uri.GetLeftPart(UriPartial.Authority)) || uri.UserInfo.Length != SiteCoverageTokens.Zero ||
            uri.Query.Length != SiteCoverageTokens.Zero || uri.Fragment.Length != SiteCoverageTokens.Zero)
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.BrowserOriginFailure);
        }

        if (path.Contains(SiteCoverageTokens.Percent, StringComparison.Ordinal) ||
            url.Contains(SiteCoverageTokens.QueryMarker, StringComparison.Ordinal) ||
            url.Contains(SiteCoverageTokens.FragmentMarker, StringComparison.Ordinal))
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidUrlFailure);
        }

        var sourcePath = SiteCoverageTokens.FeatureSourcePrefix + path[SiteCoverageTokens.FeatureUrlPrefix.Length..];
        if (sources.ContainsKey(sourcePath))
        {
            return sourcePath;
        }

        if (sourcePath.StartsWith(SiteCoverageTokens.VendorSourcePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (sourcePath.EndsWith(SiteCoverageTokens.ModuleExtension, StringComparison.Ordinal))
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.UnexpectedProductionScriptFailure);
        }

        return null;
    }

    private static bool IsWithin(string path, string directory)
    {
        var prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(prefix, comparison);
    }
}
