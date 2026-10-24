using System.Globalization;
using System.Reflection;

namespace KeyLoad.UnitTests.CodeQuality;

/// <summary>Resolves the original centrally restored execution tool from this actual test assembly.</summary>
internal sealed record CoverageToolPackage(string Version, string PackageRoot)
{
    private const int VersionComponentCount = 3;
    private const char VersionSeparator = '.';
    private const string ToolAssemblyPath = "tools/net8.0/any/dotnet-coverage.dll";
    private const char PathSeparator = '/';

    internal static CoverageToolPackage Read()
    {
        var attributes = typeof(CoverageToolPackage).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        var version = ReadSingle(attributes, CoverageProtocol.ToolVersionMetadataKey);
        var restored = ReadSingle(attributes, CoverageProtocol.ToolPackageRootMetadataKey);
        if (!IsVersion(version) || !Path.IsPathFullyQualified(restored))
        {
            throw new InvalidOperationException(CoverageProtocol.InvalidToolMetadata);
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(restored));
        if (!string.Equals(Path.GetFileName(root), version, StringComparison.Ordinal)
            || !Directory.Exists(root)
            || !File.Exists(Path.Combine(root, ToolAssemblyPath.Replace(PathSeparator, Path.DirectorySeparatorChar))))
        {
            throw new InvalidOperationException(CoverageProtocol.InvalidToolMetadata);
        }
        return new(version, root);
    }

    private static string ReadSingle(AssemblyMetadataAttribute[] attributes, string key)
    {
        string? value = null;
        foreach (var attribute in attributes)
        {
            if (!string.Equals(attribute.Key, key, StringComparison.Ordinal))
            {
                continue;
            }
            if (value is not null || string.IsNullOrWhiteSpace(attribute.Value))
            {
                throw new InvalidOperationException(CoverageProtocol.InvalidToolMetadata);
            }
            value = attribute.Value;
        }
        return value ?? throw new InvalidOperationException(CoverageProtocol.InvalidToolMetadata);
    }

    private static bool IsVersion(string value)
    {
        var components = value.Split(VersionSeparator);
        return components.Length == VersionComponentCount
            && components.All(component => int.TryParse(component, NumberStyles.None,
                CultureInfo.InvariantCulture, out var number) && number >= 0);
    }
}
