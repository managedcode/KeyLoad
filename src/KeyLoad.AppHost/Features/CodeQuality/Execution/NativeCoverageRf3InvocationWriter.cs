using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal static class NativeCoverageRf3InvocationWriter
{
    private const string ContextDirectoryName = "coverage-image-context";
    private const string InvocationFileName = "functional-coverage.image-invocation.v1.json";
    private const char NullCharacter = '\0';
    private const int MinimumContentBytes = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<NativeCoverageRf3Invocation> WriteAsync(string repositoryRoot,
        NativeCoverageRf3Run run, IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        var evidence = Path.GetDirectoryName(run.ManifestPath)
            ?? throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidRun);
        var context = Path.Combine(evidence, ContextDirectoryName);
        var baseReceipt = await NativeCoverageRf3BaseImageReceiptWriter.WriteAsync(repositoryRoot, evidence,
            run.Admission.SourceRevision, options, cancellationToken).ConfigureAwait(false);
        var baseReceiptBytes = await ReadReceiptAsync(baseReceipt.Path, execution.MaximumManifestBytes,
            execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        var serverDirectory = ResolveInputPath(repositoryRoot, run.Admission.Server.DllPath, execution.MaximumPathCharacters);
        var pdbPath = ResolveInputPath(repositoryRoot, run.Admission.Server.PdbPath, execution.MaximumPathCharacters);
        if (!string.Equals(Path.GetDirectoryName(serverDirectory), Path.GetDirectoryName(pdbPath), StringComparison.Ordinal)
            || !File.Exists(serverDirectory) || !File.Exists(pdbPath))
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
        }
        var tool = NativeCoverageToolPackage.Read();
        var value = CreateInvocation(context, Path.GetDirectoryName(serverDirectory)!, run, execution,
            baseReceipt, Convert.ToHexStringLower(SHA256.HashData(baseReceiptBytes)), tool);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (bytes.Length > execution.MaximumDescriptorBytes)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidRun);
        }
        var path = Path.Combine(evidence, InvocationFileName);
        await WriteCreateOnlyAsync(path, bytes, execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        return new(path, context, baseReceipt, run.ImageReference);
    }

    private static object CreateInvocation(string context, string serverDirectory, NativeCoverageRf3Run run,
        NativeCoverageExecutionOptions options, NativeCoverageRf3BaseImageReceipt baseReceipt,
        string baseReceiptSha256, NativeCoverageToolPackage tool)
        => new
        {
            schemaVersion = NativeCoverageRf3Protocol.InvocationSchemaVersion,
            invocationId = Guid.NewGuid().ToString(NativeCoverageRf3Protocol.GuidFormat),
            contextDirectory = context,
            serverPublishDirectory = serverDirectory,
            server = new
            {
                dllSha256 = run.Admission.Server.DllSha256,
                pdbSha256 = run.Admission.Server.PdbSha256,
                mvid = run.Admission.Server.Mvid,
                sourceReceiptPath = run.Admission.SourceManifestPath,
                sourceReceiptSha256 = run.Admission.SourceManifestSha256
            },
            baseImage = new
            {
                reference = baseReceipt.ImageReference,
                sourceReceiptPath = baseReceipt.Path,
                sourceReceiptSha256 = baseReceiptSha256
            },
            tool = new { version = tool.Version, packageRoot = tool.PackageRoot },
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

    private static string ResolveInputPath(string root, string value, int maximumCharacters)
    {
        if (value.Length > maximumCharacters || value.Contains(NullCharacter, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
        }
        var resolved = Path.GetFullPath(value, root);
        if (resolved.Length > maximumCharacters)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidManifest);
        }
        return resolved;
    }

    private static async Task<byte[]> ReadReceiptAsync(string path, int maximumBytes, int bufferBytes,
        CancellationToken token)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MinimumContentBytes || info.Length > maximumBytes)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = new byte[checked((int)info.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.Length != bytes.Length)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        return bytes;
    }

    internal static async Task WriteCreateOnlyAsync(string path, byte[] bytes, int bufferBytes, CancellationToken token)
    {
        var creation = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = bufferBytes,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows())
        {
            creation.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        await using var stream = new FileStream(path, creation);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        FlushToDisk(stream);
    }

    private static void FlushToDisk(FileStream stream) => stream.Flush(flushToDisk: true);
}

internal sealed record NativeCoverageRf3Invocation(string Path, string ContextDirectory,
    NativeCoverageRf3BaseImageReceipt BaseImage, string ImageReference);
