using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

[Orleans.GenerateSerializer]
[Orleans.Alias(ZoneTreeMetadataAliases.FormatUpgradeReceipt)]
internal sealed record ZoneTreeFormatUpgradeReceipt(
    [property: Orleans.Id(0)] int FormatVersion,
    [property: Orleans.Id(1)] string SourceDirectory,
    [property: Orleans.Id(2)] string DestinationDirectory,
    [property: Orleans.Id(3)] string SourceIdentitySha256,
    [property: Orleans.Id(4)] string SourceJournalSha256,
    [property: Orleans.Id(5)] Guid SourceNodeId,
    [property: Orleans.Id(6)] Guid SourceIncarnation,
    [property: Orleans.Id(7)] int SourceDataEpoch,
    [property: Orleans.Id(8)] int TargetDataEpoch);

internal static class ZoneTreeFormatUpgradeReceiptFile
{
    private const int NoFileAttributes = 0;
    private const int HexCharactersPerByte = 2;

    private const string InvalidReceipt = "The offline format upgrade receipt is invalid.";
    private const int MaximumReceiptBytes = 65_536;

    internal static void Write(string path, ZoneTreeFormatUpgradeReceipt receipt)
    {
        Validate(receipt);
        var payload = NativeSerialization.Serialize(receipt);
        var envelope = new ZoneTreeIdentityEnvelope(payload, SHA256.HashData(payload));
        var bytes = ZoneTreeMetadataBinary.Write(envelope, ZoneTreePersistenceFormat.FormatUpgradeReceiptMagic);
        if (bytes.Length > MaximumReceiptBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidReceipt);
        }
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            ZoneTreePersistenceFormat.IdentityBufferBytes, FileOptions.WriteThrough);
        file.Write(bytes);
        file.Flush(true);
    }

    internal static ZoneTreeFormatUpgradeReceipt Read(string path)
    {
        VerifyRegularFile(path);
        var bytes = ZoneTreeMetadataFile.Read(path, MaximumReceiptBytes, InvalidReceipt);
        var envelope = ZoneTreeMetadataBinary.Read<ZoneTreeIdentityEnvelope>(bytes.Span,
            ZoneTreePersistenceFormat.FormatUpgradeReceiptMagic, InvalidReceipt);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Checksum))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidReceipt);
        }
        var receipt = NativeSerialization.Deserialize<ZoneTreeFormatUpgradeReceipt>(envelope.Payload)
            ?? throw Errors.Fail(ErrorCode.Corruption, InvalidReceipt);
        Validate(receipt);
        return receipt;
    }

    internal static void VerifyRegularFile(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != NoFileAttributes)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, InvalidReceipt);
        }
    }

    private static void Validate(ZoneTreeFormatUpgradeReceipt receipt)
    {
        if (receipt is null || receipt.FormatVersion != ZoneTreePersistenceFormat.UpgradeReceiptVersion || !IsNormalized(receipt.SourceDirectory)
            || !IsNormalized(receipt.DestinationDirectory) || receipt.SourceNodeId == Guid.Empty
            || receipt.SourceIncarnation == Guid.Empty
            || receipt.SourceDataEpoch is not (ZoneTreePersistenceFormat.Native5DataEpoch or ZoneTreePersistenceFormat.Native6DataEpoch)
            || receipt.TargetDataEpoch != ZoneTreePersistenceFormat.CurrentDataEpoch
            || !IsDigest(receipt.SourceIdentitySha256) || !IsDigest(receipt.SourceJournalSha256))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, InvalidReceipt);
        }
    }

    private static bool IsDigest(string value)
    {
        if (value is null || value.Length != SHA256.HashSizeInBytes * HexCharactersPerByte)
        {
            return false;
        }
        try
        {
            return Convert.ToHexStringLower(Convert.FromHexString(value)) == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsNormalized(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) == path;
    }
}
