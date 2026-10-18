using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal static class NativeCoverageClusterManifestReader
{
    private const string TestImageFileNameProperty = "fileName";
    private const string TestImageHashProperty = "sha256";
    private const int MinimumContentBytes = 1;
    private const int NoUnexpectedCharacterIndex = -1;
    private static readonly SearchValues<char> HexCharacters = SearchValues.Create(NativeCoverageClusterProtocol.HexCharacters);

    internal static async Task<NativeCoverageClusterAdmission> ReadAsync(string path,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        }
        var fullPath = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(path)
            || !string.Equals(Path.GetFileName(fullPath), NativeCoverageClusterProtocol.ProductionManifestName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        }
        var bytes = await ReadBoundedAsync(fullPath, NativeCoverageClusterProtocol.MaximumSourceManifestBytes,
            execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            RequireSchema(root);
            var revision = RequireString(root, NativeCoverageClusterProtocol.SourceRevisionProperty);
            var contributors = ReadContributors(root);
            var testHash = ReadTestManifestHash(root);
            var server = ReadServerIdentity(root);
            return new(fullPath, revision, Convert.ToHexStringLower(SHA256.HashData(bytes)), testHash, server, contributors);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidManifest);
        }
    }

    private static void RequireSchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || root.GetProperty(NativeCoverageClusterProtocol.SchemaVersionProperty).GetInt32()
                != NativeCoverageClusterProtocol.ManifestSchemaVersion)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidManifest);
        }
    }

    private static List<NativeCoverageClusterContributor> ReadContributors(JsonElement root)
    {
        var result = new List<NativeCoverageClusterContributor>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty(NativeCoverageClusterProtocol.ContributorsProperty).EnumerateArray())
        {
            if (RequireString(item, NativeCoverageClusterProtocol.SuiteProperty) != NativeCoverageClusterProtocol.ClusterSuite)
            {
                continue;
            }
            var contributor = new NativeCoverageClusterContributor(
                RequireString(item, NativeCoverageClusterProtocol.ClassNameProperty),
                RequireString(item, NativeCoverageClusterProtocol.MethodNameProperty),
                RequireString(item, NativeCoverageClusterProtocol.InstanceNameProperty));
            if (!IsAllowedClass(contributor.ClassName, contributor.MethodName, contributor.InstanceName)
                || !identities.Add(string.Join(NativeCoverageClusterProtocol.IdentitySeparator, contributor.ClassName, contributor.MethodName, contributor.InstanceName)))
            {
                throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidManifest);
            }
            result.Add(contributor);
        }
        if (result.Count != NativeCoverageClusterProtocol.RequiredContributorCount)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidManifest);
        }
        return result;
    }

    private static NativeCoverageClusterServer ReadServerIdentity(JsonElement root)
    {
        foreach (var item in root.GetProperty(NativeCoverageClusterProtocol.CompiledProductsProperty).EnumerateArray())
        {
            if (RequireString(item, NativeCoverageClusterProtocol.ModuleProperty) != NativeCoverageClusterProtocol.ServerModuleName)
            {
                continue;
            }
            var identity = item.GetProperty(NativeCoverageClusterProtocol.CompiledIdentityProperty);
            var server = new NativeCoverageClusterServer(
                RequireString(identity, NativeCoverageClusterProtocol.DllPathProperty),
                RequireString(identity, NativeCoverageClusterProtocol.PdbPathProperty),
                RequireString(identity, NativeCoverageClusterProtocol.DllShaProperty),
                RequireString(identity, NativeCoverageClusterProtocol.PdbShaProperty),
                RequireString(identity, NativeCoverageClusterProtocol.MvidProperty));
            if (!IsHex(server.DllSha256) || !IsHex(server.PdbSha256)
                || !Guid.TryParseExact(server.Mvid, NativeCoverageClusterProtocol.GuidFormat, out _))
            {
                throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidManifest);
            }
            return server;
        }
        throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidManifest);
    }

    private static bool IsHex(string value) => value.Length == NativeCoverageClusterProtocol.JsonShaHexLength
        && value.AsSpan().IndexOfAnyExcept(HexCharacters) == NoUnexpectedCharacterIndex;

    private static string ReadTestManifestHash(JsonElement root)
    {
        foreach (var item in root.GetProperty(NativeCoverageClusterProtocol.CompiledTestsProperty).EnumerateArray())
        {
            if (RequireString(item, NativeCoverageClusterProtocol.SuiteProperty) == NativeCoverageClusterProtocol.ClusterSuite
                && RequireString(item, TestImageFileNameProperty) == NativeCoverageClusterProtocol.CompiledTestsManifestName)
            {
                var hash = RequireString(item, TestImageHashProperty);
                if (!IsHex(hash))
                {
                    throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidManifest);
                }
                return hash;
            }
        }
        throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidManifest);
    }

    private static bool IsAllowedClass(string className, string methodName, string instanceName)
        => NativeCoverageClusterContributorCatalog.Contributors.Contains(new(className, methodName, instanceName));

    private static string RequireString(JsonElement element, string property)
    {
        var value = element.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException() : value;
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximumBytes, int bufferBytes,
        CancellationToken token)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MinimumContentBytes || info.Length > maximumBytes)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidManifest);
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = new byte[checked((int)info.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.Length != bytes.Length)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidManifest);
        }
        return bytes;
    }
}
