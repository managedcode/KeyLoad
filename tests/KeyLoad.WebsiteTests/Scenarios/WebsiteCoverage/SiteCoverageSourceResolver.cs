using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteCoverageSourceResolver
{
    public static (bool HasValue, string? Value) ResolveScriptSource(JsonElement script, string runtime, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources, IReadOnlySet<string>? browserOrigins)
    {
        SiteCoverageJson.RequireObject(script, SiteCoverageJson.RuntimeFields.ScriptRequired,
            SiteCoverageJson.RuntimeFields.ScriptOptional);
        _ = SiteCoverageJson.GetString(script, SiteCoverageTokens.ScriptId);
        var url = SiteCoverageJson.GetString(script, SiteCoverageTokens.Url);
        if (url.Length == SiteCoverageTokens.Zero)
        {
            return (false, default(string?));
        }

        return runtime == SiteCoverageTokens.NodeRuntime
            ? ((string?)((ResolveNodeSource(url, repository, sources)).Value)) is { } outcomeValue2 ? (true, outcomeValue2) : (false, default(string?))
            : ((string?)((ResolveBrowserSource(url, browserOrigins!, sources)).Value)) is { } outcomeValue3 ? (true, outcomeValue3) : (false, default(string?));
    }

    private static (bool HasValue, string? Value) ResolveNodeSource(string url, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != SiteCoverageTokens.FileScheme ||
            uri.Query.Length != SiteCoverageTokens.Zero || uri.Fragment.Length != SiteCoverageTokens.Zero ||
            uri.UserInfo.Length != SiteCoverageTokens.Zero)
        {
            return (false, default(string?));
        }

        var fullPath = Path.GetFullPath(uri.LocalPath);
        var matched = sources.Keys.FirstOrDefault(path => SiteCoverageSourcePaths.PathsEqual(
            Path.Combine(repository, path.Replace(SiteCoverageTokens.RelativeSeparator, Path.DirectorySeparatorChar)), fullPath));
        if (matched is not null)
        {
            return (true, ((string?)(matched)));
        }

        var siteRoot = Path.GetFullPath(Path.Combine(repository, SiteCoverageTokens.SiteSourcePrefix));
        var evidenceRoot = Path.GetFullPath(Path.Combine(repository, SitePublicationTokens.EvidenceToolsPrefix));
        var portablePath = fullPath.Replace(Path.DirectorySeparatorChar, SiteCoverageTokens.RelativeSeparator);
        if ((IsWithin(fullPath, siteRoot) && !portablePath.Contains(SiteCoverageTokens.VendorSourcePrefix,
                StringComparison.Ordinal)) ||
            (IsWithin(fullPath, evidenceRoot) && SiteCoverageSourceInventory.IsTrackedEvidenceModule(portablePath)))
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.UnexpectedProductionScriptFailure);
        }

        return (false, default(string?));
    }

    private static (bool HasValue, string? Value) ResolveBrowserSource(string url, IReadOnlySet<string> browserOrigins,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != SiteCoverageTokens.HttpScheme)
        {
            return (false, default(string?));
        }

        var path = uri.AbsolutePath;
        var shared = path.StartsWith("/scripts/Features/BenchmarkComparisons/", StringComparison.Ordinal);
        if (!shared && !path.StartsWith(SiteCoverageTokens.FeatureUrlPrefix, StringComparison.Ordinal))
        {
            return (false, default(string?));
        }

        if (!browserOrigins.Contains(uri.GetLeftPart(UriPartial.Authority)) || uri.UserInfo.Length != SiteCoverageTokens.Zero ||
            uri.Query.Length != SiteCoverageTokens.Zero || uri.Fragment.Length != SiteCoverageTokens.Zero)
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.BrowserOriginFailure);
        }

        if (path.Contains(SiteCoverageTokens.Percent, StringComparison.Ordinal) ||
            url.Contains(SiteCoverageTokens.QueryMarker, StringComparison.Ordinal) ||
            url.Contains(SiteCoverageTokens.FragmentMarker, StringComparison.Ordinal))
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.InvalidUrlFailure);
        }

        var sourcePath = shared ? path[1..] : SiteCoverageTokens.FeatureSourcePrefix + path[SiteCoverageTokens.FeatureUrlPrefix.Length..];
        if (sources.ContainsKey(sourcePath))
        {
            return (true, ((string?)(sourcePath)));
        }

        if (sourcePath.StartsWith(SiteCoverageTokens.VendorSourcePrefix, StringComparison.Ordinal))
        {
            return (false, default(string?));
        }

        if (sourcePath.EndsWith(SiteCoverageTokens.ModuleExtension, StringComparison.Ordinal))
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.UnexpectedProductionScriptFailure);
        }

        return (false, default(string?));
    }

    private static bool IsWithin(string path, string directory)
    {
        var prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(prefix, comparison);
    }
}
