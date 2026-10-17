using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageRf3BaseImageReceipt(string Path, string SourcePath,
    string SourceSha256, string ImageReference);

internal static class NativeCoverageRf3BaseImageReceiptWriter
{
    private const string DockerfileRelativePath = "Dockerfile";
    private const string FromPrefix = "FROM ";
    private const string AspNetPrefix = "mcr.microsoft.com/dotnet/aspnet:";
    private const string DigestMarker = "@sha256:";
    private const string RuntimeStage = "runtime";
    private const string AsKeyword = "AS";
    private const char LineFeed = '\n';
    private const char SpaceSeparator = ' ';
    private const string ReceiptName = "functional-coverage.base-image.v1.json";
    private const int FromDirectiveMinimumTokens = 2;
    private const int RuntimeDirectiveTokens = 4;
    private const int FromTokenIndex = 0;
    private const int ImageTokenIndex = 1;
    private const int AsTokenIndex = 2;
    private const int StageTokenIndex = 3;
    private const int RequiredRuntimeStages = 1;
    private const int SelectedRuntimeStageIndex = 0;
    private const int MinimumContentBytes = 1;
    private const int NoUnexpectedCharacterIndex = -1;
    private static readonly SearchValues<char> ShaHex = SearchValues.Create(NativeCoverageRf3Protocol.HexCharacters);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<NativeCoverageRf3BaseImageReceipt> WriteAsync(string repositoryRoot,
        string evidenceDirectory, string sourceRevision, IOptions<NativeCoverageExecutionOptions> options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRevision);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        var sourcePath = Path.GetFullPath(Path.Combine(repositoryRoot, DockerfileRelativePath));
        var bytes = await ReadBoundedAsync(sourcePath, execution.MaximumManifestBytes,
            execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        var image = ReadAspNetImageReference(bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Directory.CreateDirectory(evidenceDirectory);
        var receiptPath = Path.Combine(evidenceDirectory, ReceiptName);
        var receipt = new BaseImageReceipt(NativeCoverageRf3Protocol.BaseImageSchemaVersion,
            sourceRevision, DockerfileRelativePath, hash, image);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions);
        if (serialized.Length > execution.MaximumManifestBytes)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        await WriteCreateOnlyAsync(receiptPath, serialized, execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        return new(receiptPath, sourcePath, hash, image);
    }

    internal static async Task VerifyUnchangedAsync(NativeCoverageRf3BaseImageReceipt receipt,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        var bytes = await ReadBoundedAsync(receipt.SourcePath, execution.MaximumManifestBytes,
            execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(hash, receipt.SourceSha256, StringComparison.Ordinal)
            || !string.Equals(ReadAspNetImageReference(bytes), receipt.ImageReference, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
    }

    private static string ReadAspNetImageReference(byte[] bytes)
    {
        var matches = new List<string>();
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        foreach (var line in text.Split(LineFeed))
        {
            var parts = line.Trim().Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < FromDirectiveMinimumTokens
                || !string.Equals(parts[FromTokenIndex], FromPrefix.TrimEnd(), StringComparison.Ordinal)
                || !parts[ImageTokenIndex].StartsWith(AspNetPrefix, StringComparison.Ordinal))
            {
                continue;
            }
            if (parts.Length != RuntimeDirectiveTokens || !string.Equals(parts[AsTokenIndex], AsKeyword, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(parts[StageTokenIndex], RuntimeStage, StringComparison.OrdinalIgnoreCase)
                || !IsDigestReference(parts[ImageTokenIndex]))
            {
                throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
            }
            matches.Add(parts[ImageTokenIndex]);
        }
        if (matches.Count != RequiredRuntimeStages)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        return matches[SelectedRuntimeStageIndex];
    }

    private static bool IsDigestReference(string value)
    {
        var index = value.LastIndexOf(DigestMarker, StringComparison.Ordinal);
        return index > AspNetPrefix.Length && value.Length - index - DigestMarker.Length == NativeCoverageRf3Protocol.JsonShaHexLength
            && value.AsSpan(index + DigestMarker.Length).IndexOfAnyExcept(ShaHex) == NoUnexpectedCharacterIndex;
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximumBytes, int bufferBytes,
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
        try
        {
            await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        }
        catch (EndOfStreamException error)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection, error);
        }
        if (stream.Length != bytes.Length)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        return bytes;
    }

    private static async Task WriteCreateOnlyAsync(string path, byte[] bytes, int bufferBytes, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferBytes, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        FlushToDisk(stream);
    }

    private static void FlushToDisk(FileStream stream) => stream.Flush(flushToDisk: true);

    private sealed record BaseImageReceipt(int SchemaVersion, string SourceRevision,
        string SourcePath, string SourceSha256, string ImageReference);
}
