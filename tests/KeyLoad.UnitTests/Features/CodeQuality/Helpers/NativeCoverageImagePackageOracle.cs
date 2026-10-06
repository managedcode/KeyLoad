using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageImagePackageOracle
{
    private static readonly string[] NativeFiles =
    [
        NativeCoverageImageConstants.LinuxInstrumentationEngine,
        NativeCoverageImageConstants.LinuxCoverageInstrumentationMethod,
        NativeCoverageImageConstants.LinuxCoverageConfig
    ];
    private static readonly string[] LicenseFiles =
        [NativeCoverageImageConstants.License, NativeCoverageImageConstants.ThirdPartyNotices];
    private static readonly JsonSerializerOptions DigestOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static string AddFiles(Dictionary<string, NativeCoverageImageExpectedFile> files,
        NativeCoverageImageFixture fixture)
    {
        var package = fixture.Tool;
        var toolRoot = Path.Combine(package.PackageRoot,
            NativeCoverageImageConstants.DotnetCoverageToolDirectory.Replace('/', Path.DirectorySeparatorChar));
        var relativeAssets = ReadAssets(Path.Combine(toolRoot, NativeCoverageImageConstants.DotnetCoverageDeps), package.Version,
            fixture.Options.Coverage.Value.MaximumManifestBytes);
        AddAssets(relativeAssets, fixture.Options.Coverage.Value.MaximumFiles);
        var packageEntries = new List<ClosureEntry>();
        foreach (var relative in relativeAssets.Order(StringComparer.Create(CultureInfo.GetCultureInfo("en"), false)))
        {
            var source = Path.Combine(toolRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var entry = NativeCoverageImageExpectedFiles.FromPath("tool/" + relative, source,
                fixture.Options.Coverage.Value.MaximumFileBytes);
            Add(files, entry);
            packageEntries.Add(new(entry.RelativePath, entry.Mode, entry.Length, entry.Sha256));
        }
        foreach (var name in LicenseFiles)
        {
            var entry = NativeCoverageImageExpectedFiles.FromPath("license/" + name,
                Path.Combine(package.PackageRoot, name), fixture.Options.Coverage.Value.MaximumFileBytes);
            Add(files, entry);
        }
        var digestBytes = JsonSerializer.SerializeToUtf8Bytes(packageEntries, DigestOptions);
        return Convert.ToHexStringLower(SHA256.HashData(digestBytes));
    }

    internal static (string Sha256, string Sha512) ReadArchiveIdentity(NativeCoverageToolPackage package,
        int maximumFileBytes, int maximumManifestBytes)
    {
        var archive = Path.Combine(package.PackageRoot, $"{NativeCoverageImageConstants.CoveragePackageId}.{package.Version}.nupkg");
        var info = new FileInfo(archive);
        if (!info.Exists || info.Length < 0 || info.Length > maximumFileBytes)
        {
            throw new InvalidDataException("The restored native coverage archive is outside its supported read bound.");
        }
        using var stream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha512 = SHA512.Create();
        var digest = Convert.ToBase64String(sha512.ComputeHash(stream));
        var sidecarPath = archive + ".sha512";
        var sidecar = System.Text.Encoding.ASCII.GetString(
            NativeCoverageImageOracleSupport.ReadBounded(sidecarPath, maximumManifestBytes)).Trim();
        if (sidecar != digest)
        {
            throw new InvalidDataException("The restored native coverage archive differs from its NuGet receipt.");
        }
        stream.Position = 0;
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        return (sha256, digest);
    }

    private static HashSet<string> ReadAssets(string depsPath, string version, int maximumManifestBytes)
    {
        if (new FileInfo(depsPath).Length > maximumManifestBytes)
        { throw new InvalidDataException("The native coverage dependency manifest exceeds its configured bound."); }
        using var stream = File.OpenRead(depsPath);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        { MaxDepth = NativeCoverageImageConstants.MaximumJsonDepth });
        var target = document.RootElement.GetProperty(NativeCoverageImageFields.Targets)
            .GetProperty(NativeCoverageImageConstants.DotnetCoverageTargetFramework);
        var packageEntry = target.GetProperty($"{NativeCoverageImageConstants.CoveragePackageId}/{version}");
        var assets = new HashSet<string>(StringComparer.Ordinal)
        {
            NativeCoverageImageConstants.DotnetCoverageDeps,
            NativeCoverageImageConstants.DotnetCoverageRuntime
        };
        foreach (var library in target.EnumerateObject().Select(item => item.Value))
        {
            AddAssetGroup(library, "runtime", assets, false);
            AddAssetGroup(library, "resources", assets, true);
        }
        foreach (var name in NativeFiles)
        {
            assets.Add(NativeCoverageImageConstants.LinuxNativeDirectory + "/" + name);
        }
        if (packageEntry.ValueKind != JsonValueKind.Object || assets.Count == 0)
        {
            throw new InvalidDataException("The restored native coverage dependency manifest is invalid.");
        }
        return assets;
    }

    private static void AddAssetGroup(JsonElement library, string groupName, HashSet<string> assets, bool resources)
    {
        if (!library.TryGetProperty(groupName, out var group) || group.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        foreach (var asset in group.EnumerateObject())
        {
            var localPath = asset.Value.ValueKind == JsonValueKind.Object
                && asset.Value.TryGetProperty(NativeCoverageImageFields.LocalPath, out var local)
                ? local.GetString()
                : resources && asset.Value.ValueKind == JsonValueKind.Object
                    && asset.Value.TryGetProperty(NativeCoverageImageFields.Locale, out var locale)
                    ? Path.Combine(locale.GetString()!, Path.GetFileName(asset.Name)).Replace(Path.DirectorySeparatorChar, '/')
                    : Path.GetFileName(asset.Name);
            if (string.IsNullOrWhiteSpace(localPath) || !IsSafeRelative(localPath))
            {
                throw new InvalidDataException("The native coverage dependency path is invalid.");
            }
            assets.Add(localPath);
        }
    }

    private static void AddAssets(HashSet<string> assets, int maximumFiles)
    {
        if (assets.Count > maximumFiles)
        {
            throw new InvalidDataException("The restored native coverage dependency closure exceeds its file count bound.");
        }
    }

    private static bool IsSafeRelative(string value)
        => !Path.IsPathRooted(value) && !value.Contains('\\', StringComparison.Ordinal) && value.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));

    private static void Add(Dictionary<string, NativeCoverageImageExpectedFile> files,
        NativeCoverageImageExpectedFile file)
    {
        if (!files.TryAdd(file.RelativePath, file))
        {
            throw new InvalidDataException("The independent package inventory contains a duplicate path.");
        }
    }

    private sealed record ClosureEntry(string Path, int Mode, long Length, string Sha256);
}
