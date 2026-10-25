using System.Text.Json;

namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal static class SiteCoverageSourceResolver
{
    public static global::ManagedCode.Communication.Result<string?> ResolveScriptSource(JsonElement script, string runtime, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources, IReadOnlySet<string>? browserOrigins)
    {
        SiteCoverageJson.RequireObject(script, SiteCoverageJson.RuntimeFields.ScriptRequired,
            SiteCoverageJson.RuntimeFields.ScriptOptional);
        _ = SiteCoverageJson.GetString(script, SiteCoverageTokens.ScriptId);
        var url = SiteCoverageJson.GetString(script, SiteCoverageTokens.Url);
        if (url.Length == SiteCoverageTokens.Zero)
        {
            return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ResolveScriptSource));
        }

        return runtime == SiteCoverageTokens.NodeRuntime
            ? ((string?)((ResolveNodeSource(url, repository, sources)).Value)) is { } outcomeValue2 ? global::ManagedCode.Communication.Result<string?>.Succeed(outcomeValue2) : global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ResolveScriptSource))
            : ((string?)((ResolveBrowserSource(url, browserOrigins!, sources)).Value)) is { } outcomeValue3 ? global::ManagedCode.Communication.Result<string?>.Succeed(outcomeValue3) : global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ResolveScriptSource));
    }

    private static global::ManagedCode.Communication.Result<string?> ResolveNodeSource(string url, string repository,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != SiteCoverageTokens.FileScheme ||
            uri.Query.Length != SiteCoverageTokens.Zero || uri.Fragment.Length != SiteCoverageTokens.Zero ||
            uri.UserInfo.Length != SiteCoverageTokens.Zero)
        {
            return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ResolveNodeSource));
        }

        var fullPath = Path.GetFullPath(uri.LocalPath);
        var matched = sources.Keys.FirstOrDefault(path => SiteCoverageSourcePaths.PathsEqual(
            Path.Combine(repository, path.Replace(SiteCoverageTokens.RelativeSeparator, Path.DirectorySeparatorChar)), fullPath));
        if (matched is not null)
        {
            return global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(matched)));
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

        return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ResolveNodeSource));
    }

    private static global::ManagedCode.Communication.Result<string?> ResolveBrowserSource(string url, IReadOnlySet<string> browserOrigins,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != SiteCoverageTokens.HttpScheme)
        {
            return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ResolveBrowserSource));
        }

        var path = uri.AbsolutePath;
        var shared = path.StartsWith("/scripts/Features/BenchmarkComparisons/", StringComparison.Ordinal);
        if (!shared && !path.StartsWith(SiteCoverageTokens.FeatureUrlPrefix, StringComparison.Ordinal))
        {
            return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ResolveBrowserSource));
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
            return global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(sourcePath)));
        }

        if (sourcePath.StartsWith(SiteCoverageTokens.VendorSourcePrefix, StringComparison.Ordinal))
        {
            return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ResolveBrowserSource));
        }

        if (sourcePath.EndsWith(SiteCoverageTokens.ModuleExtension, StringComparison.Ordinal))
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.UnexpectedProductionScriptFailure);
        }

        return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ResolveBrowserSource));
    }

    private static bool IsWithin(string path, string directory)
    {
        var prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(prefix, comparison);
    }
}
