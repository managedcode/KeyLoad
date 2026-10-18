using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal static class NativeCoverageClusterBaseImageReceiptWriter
{
    private const string ContainerSettingsRelativePath = "Directory.Build.props";
    private const string ContainerBaseImageElement = "ContainerBaseImage";
    private const string AspNetPrefix = "mcr.microsoft.com/dotnet/aspnet:";
    private const string DigestMarker = "@sha256:";
    private const string ReceiptName = "functional-coverage.base-image.v1.json";
    private const int RequiredRuntimeStages = 1;
    private const int SelectedRuntimeStageIndex = 0;
    private const int MinimumContentBytes = 1;
    private const int NoUnexpectedCharacterIndex = -1;
    private static readonly SearchValues<char> ShaHex = SearchValues.Create(NativeCoverageClusterProtocol.HexCharacters);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<NativeCoverageClusterBaseImageReceipt> WriteAsync(string repositoryRoot,
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
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        }
        var sourcePath = Path.GetFullPath(Path.Combine(repositoryRoot, ContainerSettingsRelativePath));
        var bytes = await ReadBoundedAsync(sourcePath, execution.MaximumManifestBytes,
            execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        var image = ReadAspNetImageReference(bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Directory.CreateDirectory(evidenceDirectory);
        var receiptPath = Path.Combine(evidenceDirectory, ReceiptName);
        var receipt = new BaseImageReceipt(NativeCoverageClusterProtocol.BaseImageSchemaVersion,
            sourceRevision, ContainerSettingsRelativePath, hash, image);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions);
        if (serialized.Length > execution.MaximumManifestBytes)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        }
        await WriteCreateOnlyAsync(receiptPath, serialized, execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        return new(receiptPath, sourcePath, hash, image);
    }

    internal static async Task VerifyUnchangedAsync(NativeCoverageClusterBaseImageReceipt receipt,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        }
        var bytes = await ReadBoundedAsync(receipt.SourcePath, execution.MaximumManifestBytes,
            execution.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(hash, receipt.SourceSha256, StringComparison.Ordinal)
            || !string.Equals(ReadAspNetImageReference(bytes), receipt.ImageReference, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        }
    }

    private static string ReadAspNetImageReference(byte[] bytes)
    {
        try
        {
            using var source = new StringReader(StrictUtf8.GetString(bytes));
            using var reader = XmlReader.Create(source, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = bytes.Length
            });
            var matches = XDocument.Load(reader).Descendants(ContainerBaseImageElement)
                .Select(element => element.Value).ToArray();
            if (matches.Length != RequiredRuntimeStages || !matches[SelectedRuntimeStageIndex].StartsWith(AspNetPrefix, StringComparison.Ordinal)
                || !IsDigestReference(matches[SelectedRuntimeStageIndex]))
            {
                throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
            }
            return matches[SelectedRuntimeStageIndex];
        }
        catch (Exception error) when (error is DecoderFallbackException or XmlException)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection, error);
        }
    }

    private static bool IsDigestReference(string value)
    {
        var index = value.LastIndexOf(DigestMarker, StringComparison.Ordinal);
        return index > AspNetPrefix.Length && value.Length - index - DigestMarker.Length == NativeCoverageClusterProtocol.JsonShaHexLength
                                           && value.AsSpan(index + DigestMarker.Length).IndexOfAnyExcept(ShaHex) == NoUnexpectedCharacterIndex;
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximumBytes, int bufferBytes,
        CancellationToken token)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MinimumContentBytes || info.Length > maximumBytes)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
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
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection, error);
        }
        if (stream.Length != bytes.Length)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
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
