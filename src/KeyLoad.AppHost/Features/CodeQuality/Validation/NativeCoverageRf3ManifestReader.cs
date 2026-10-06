using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal static class NativeCoverageRf3ManifestReader
{
    private const string TestImageFileNameProperty = "fileName";
    private const string TestImageHashProperty = "sha256";
    private const int MinimumContentBytes = 1;
    private const int NoUnexpectedCharacterIndex = -1;
    private static readonly SearchValues<char> HexCharacters = SearchValues.Create(NativeCoverageRf3Protocol.HexCharacters);

    internal static async Task<NativeCoverageRf3Admission> ReadAsync(string path,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        var fullPath = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(path)
            || !string.Equals(Path.GetFileName(fullPath), NativeCoverageRf3Protocol.ProductionManifestName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        var bytes = await ReadBoundedAsync(fullPath, NativeCoverageRf3Protocol.MaximumSourceManifestBytes,
            execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            RequireSchema(root);
            var revision = RequireString(root, NativeCoverageRf3Protocol.SourceRevisionProperty);
            var contributors = ReadContributors(root);
            var testHash = ReadTestManifestHash(root);
            var server = ReadServerIdentity(root);
            return new(fullPath, revision, Convert.ToHexStringLower(SHA256.HashData(bytes)), testHash, server, contributors);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
        }
    }

    private static void RequireSchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || root.GetProperty(NativeCoverageRf3Protocol.SchemaVersionProperty).GetInt32()
                != NativeCoverageRf3Protocol.ManifestSchemaVersion)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
        }
    }

    private static List<NativeCoverageRf3Contributor> ReadContributors(JsonElement root)
    {
        var result = new List<NativeCoverageRf3Contributor>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty(NativeCoverageRf3Protocol.ContributorsProperty).EnumerateArray())
        {
            if (RequireString(item, NativeCoverageRf3Protocol.SuiteProperty) != NativeCoverageRf3Protocol.Rf3Suite)
            {
                continue;
            }
            var contributor = new NativeCoverageRf3Contributor(
                RequireString(item, NativeCoverageRf3Protocol.ClassNameProperty),
                RequireString(item, NativeCoverageRf3Protocol.MethodNameProperty),
                RequireString(item, NativeCoverageRf3Protocol.InstanceNameProperty));
            if (!IsAllowedClass(contributor.ClassName, contributor.MethodName, contributor.InstanceName)
                || !identities.Add(string.Join(NativeCoverageRf3Protocol.IdentitySeparator, contributor.ClassName, contributor.MethodName, contributor.InstanceName)))
            {
                throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
            }
            result.Add(contributor);
        }
        if (result.Count != NativeCoverageRf3Protocol.RequiredContributorCount)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
        }
        return result;
    }

    private static NativeCoverageRf3Server ReadServerIdentity(JsonElement root)
    {
        foreach (var item in root.GetProperty(NativeCoverageRf3Protocol.CompiledProductsProperty).EnumerateArray())
        {
            if (RequireString(item, NativeCoverageRf3Protocol.ModuleProperty) != NativeCoverageRf3Protocol.ServerModuleName)
            {
                continue;
            }
            var identity = item.GetProperty(NativeCoverageRf3Protocol.CompiledIdentityProperty);
            var server = new NativeCoverageRf3Server(
                RequireString(identity, NativeCoverageRf3Protocol.DllPathProperty),
                RequireString(identity, NativeCoverageRf3Protocol.PdbPathProperty),
                RequireString(identity, NativeCoverageRf3Protocol.DllShaProperty),
                RequireString(identity, NativeCoverageRf3Protocol.PdbShaProperty),
                RequireString(identity, NativeCoverageRf3Protocol.MvidProperty));
            if (!IsHex(server.DllSha256) || !IsHex(server.PdbSha256)
                || !Guid.TryParseExact(server.Mvid, NativeCoverageRf3Protocol.GuidFormat, out _))
            {
                throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
            }
            return server;
        }
        throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
    }

    private static bool IsHex(string value) => value.Length == NativeCoverageRf3Protocol.JsonShaHexLength
        && value.AsSpan().IndexOfAnyExcept(HexCharacters) == NoUnexpectedCharacterIndex;

    private static string ReadTestManifestHash(JsonElement root)
    {
        foreach (var item in root.GetProperty(NativeCoverageRf3Protocol.CompiledTestsProperty).EnumerateArray())
        {
            if (RequireString(item, NativeCoverageRf3Protocol.SuiteProperty) == NativeCoverageRf3Protocol.Rf3Suite
                && RequireString(item, TestImageFileNameProperty) == NativeCoverageRf3Protocol.CompiledTestsManifestName)
            {
                var hash = RequireString(item, TestImageHashProperty);
                if (!IsHex(hash))
                {
                    throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
                }
                return hash;
            }
        }
        throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
    }

    private static bool IsAllowedClass(string className, string methodName, string instanceName)
        => instanceName == methodName && (className, methodName) switch
        {
            (NativeCoverageRf3Protocol.QueryContributorClass, NativeCoverageRf3Protocol.QueryContributorMethod) => true,
            (NativeCoverageRf3Protocol.CrudContributorClass, NativeCoverageRf3Protocol.CrudCreatePatchDeleteMethod) => true,
            (NativeCoverageRf3Protocol.CrudContributorClass, NativeCoverageRf3Protocol.CrudPatchDeleteCreateMethod) => true,
            (NativeCoverageRf3Protocol.CrudContributorClass, NativeCoverageRf3Protocol.CrudStaleReplacementMethod) => true,
            _ => false
        };

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
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = new byte[checked((int)info.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.Length != bytes.Length)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
        }
        return bytes;
    }
}
