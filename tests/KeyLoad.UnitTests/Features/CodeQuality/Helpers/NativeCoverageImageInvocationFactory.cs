using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed record NativeCoverageImageInvocation(string Path, string InvocationId, string ContextPath,
    string ServerDirectory, string ServerDllSha256, string ServerPdbSha256, string Mvid,
    string BaseReference, string SourceReceiptPath, string BaseReceiptPath);

internal static partial class NativeCoverageImageInvocationFactory
{
    private const string DockerfilePinPattern = "^FROM (?<reference>mcr\\.microsoft\\.com/dotnet/aspnet:[^\\s]+@sha256:[0-9a-f]{64})\\s+AS runtime$";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<NativeCoverageImageInvocation> CreateAsync(NativeCoverageImageFixture fixture,
        string serverDirectory, string contextPath)
    {
        var dllPath = Path.Combine(serverDirectory, NativeCoverageImageConstants.ServerDll);
        var pdbPath = Path.Combine(serverDirectory, NativeCoverageImageConstants.ServerPdb);
        var maximumFileBytes = fixture.Options.Coverage.Value.MaximumFileBytes;
        var dllHash = Hash(dllPath, maximumFileBytes);
        var pdbHash = Hash(pdbPath, maximumFileBytes);
        var mvid = ReadMvid(dllPath, maximumFileBytes);
        var sourceReceipt = WriteServerReceipt(fixture, serverDirectory, dllHash, pdbHash, mvid);
        var baseReference = ReadPinnedImage(fixture.Options.Coverage.Value.MaximumManifestBytes);
        var baseReceipt = WriteBaseReceipt(fixture, baseReference);
        var descriptorPath = Path.Combine(fixture.Inputs, $"invocation-{Guid.NewGuid():N}.json");
        await WriteDescriptorAsync(descriptorPath, fixture, serverDirectory, contextPath, dllHash, pdbHash, mvid,
            baseReference, sourceReceipt, baseReceipt).ConfigureAwait(false);
        var invocationId = ReadInvocationId(descriptorPath, fixture.Options.Coverage.Value.MaximumDescriptorBytes);
        return new(descriptorPath, invocationId, contextPath, serverDirectory, dllHash, pdbHash, mvid,
            baseReference, sourceReceipt, baseReceipt);
    }

    private static string WriteServerReceipt(NativeCoverageImageFixture fixture, string serverDirectory,
        string dllHash, string pdbHash, string mvid)
    {
        var path = Path.Combine(fixture.Inputs, $"{NativeCoverageImageConstants.ServerReceiptName}-{Guid.NewGuid():N}");
        var receipt = new
        {
            schemaVersion = NativeCoverageImageConstants.SchemaVersion,
            evidenceKind = NativeCoverageImageConstants.LocalEvidenceKind,
            serverDll = Path.GetRelativePath(NativeCoverageImageFixture.RepositoryRoot,
                Path.Combine(serverDirectory, NativeCoverageImageConstants.ServerDll)),
            serverDllSha256 = dllHash,
            serverPdb = Path.GetRelativePath(NativeCoverageImageFixture.RepositoryRoot,
                Path.Combine(serverDirectory, NativeCoverageImageConstants.ServerPdb)),
            serverPdbSha256 = pdbHash,
            moduleVersionId = mvid
        };
        WritePrivate(path, JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions), fixture.Options.Coverage.Value.MaximumManifestBytes);
        return path;
    }

    private static string WriteBaseReceipt(NativeCoverageImageFixture fixture, string reference)
    {
        var sourcePath = Path.Combine(NativeCoverageImageFixture.RepositoryRoot, NativeCoverageImageConstants.Dockerfile);
        var path = Path.Combine(fixture.Inputs, $"{NativeCoverageImageConstants.BaseReceiptName}-{Guid.NewGuid():N}");
        var receipt = new
        {
            schemaVersion = NativeCoverageImageConstants.SchemaVersion,
            evidenceKind = NativeCoverageImageConstants.LocalEvidenceKind,
            sourcePath = NativeCoverageImageConstants.Dockerfile,
            sourceSha256 = Hash(sourcePath, fixture.Options.Coverage.Value.MaximumManifestBytes),
            imageReference = reference
        };
        WritePrivate(path, JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions), fixture.Options.Coverage.Value.MaximumManifestBytes);
        return path;
    }

    private static string ReadPinnedImage(int maximumBytes)
    {
        var path = Path.Combine(NativeCoverageImageFixture.RepositoryRoot, NativeCoverageImageConstants.Dockerfile);
        if (new FileInfo(path).Length > maximumBytes)
        { throw new InvalidDataException("The source Dockerfile exceeds its configured metadata bound."); }
        var lines = System.Text.Encoding.UTF8.GetString(NativeCoverageImageOracleSupport.ReadBounded(path, maximumBytes))
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var matches = lines.Select(line => DockerfilePin().Match(line)).Where(match => match.Success).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException("The repository Dockerfile does not have one pinned ASP.NET runtime image.");
        }
        return matches[0].Groups[NativeCoverageImageFields.Reference].Value;
    }

    private static async Task WriteDescriptorAsync(string path, NativeCoverageImageFixture fixture,
        string serverDirectory, string contextPath, string dllHash, string pdbHash, string mvid,
        string baseReference, string serverReceipt, string baseReceipt)
    {
        var options = fixture.Options.Coverage.Value;
        var descriptor = new
        {
            schemaVersion = NativeCoverageImageConstants.SchemaVersion,
            invocationId = Guid.NewGuid().ToString("D"),
            contextDirectory = contextPath,
            serverPublishDirectory = serverDirectory,
            server = new
            {
                dllSha256 = dllHash,
                pdbSha256 = pdbHash,
                mvid,
                sourceReceiptPath = serverReceipt,
                sourceReceiptSha256 = Hash(serverReceipt, options.MaximumManifestBytes)
            },
            baseImage = new
            {
                reference = baseReference,
                sourceReceiptPath = baseReceipt,
                sourceReceiptSha256 = Hash(baseReceipt, options.MaximumManifestBytes)
            },
            tool = new { version = fixture.Tool.Version, packageRoot = fixture.Tool.PackageRoot },
            bounds = new
            {
                maximumFiles = options.MaximumFiles,
                maximumTotalBytes = options.MaximumTotalBytes,
                maximumFileBytes = options.MaximumFileBytes,
                maximumPathCharacters = options.MaximumPathCharacters,
                maximumManifestBytes = options.MaximumManifestBytes,
                readBufferBytes = options.ReadBufferBytes,
                shutdownSeconds = checked((int)options.ShutdownTimeout.TotalSeconds),
                settlementSeconds = checked((int)options.SettlementTimeout.TotalSeconds),
                maximumReportBytes = options.MaximumReportBytes
            }
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(descriptor, JsonOptions);
        if (bytes.Length > options.MaximumDescriptorBytes)
        { throw new InvalidDataException("The test image invocation exceeds its admitted descriptor bound."); }
        await NativeCoverageRf3InvocationWriter.WriteCreateOnlyAsync(path, bytes, options.ReadBufferBytes,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
    }

    private static string ReadMvid(string path, int maximumBytes)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > maximumBytes)
        { throw new InvalidDataException("The observed Server assembly exceeds its configured file bound."); }
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D");
    }

    private static string Hash(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 0 || info.Length > maximumBytes)
        {
            throw new InvalidDataException("An observed native image input exceeds its configured file bound.");
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string ReadInvocationId(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > maximumBytes)
        { throw new InvalidDataException("The image invocation exceeds its descriptor bound."); }
        using var document = JsonDocument.Parse(NativeCoverageImageOracleSupport.ReadBounded(path, maximumBytes), new JsonDocumentOptions
        { MaxDepth = NativeCoverageImageConstants.MaximumJsonDepth });
        return document.RootElement.GetProperty(NativeCoverageImageFields.InvocationId).GetString()
            ?? throw new InvalidDataException("The image invocation identifier is missing.");
    }

    private static void WritePrivate(string path, byte[] bytes, int maximumBytes)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Native coverage image permissions require a Unix filesystem.");
        }
        if (bytes.Length > maximumBytes)
        { throw new InvalidDataException("A source-bound test receipt exceeds its captured bound."); }
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [GeneratedRegex(DockerfilePinPattern, RegexOptions.CultureInvariant)]
    private static partial Regex DockerfilePin();
}
