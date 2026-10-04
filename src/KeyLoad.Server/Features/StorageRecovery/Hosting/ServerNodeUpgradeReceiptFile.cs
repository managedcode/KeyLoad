using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeReceiptFile
{
    private const int HeaderBytes = sizeof(ulong) + sizeof(int);

    internal static void Write<T>(string path, T receipt, ulong magic)
    {
        var payload = NativeSerialization.Serialize(receipt);
        var envelope = NativeSerialization.Serialize(new ServerNodeUpgradeEnvelope(payload, SHA256.HashData(payload)));
        if (envelope.Length > ServerNodeUpgradeProtocol.MaximumReceiptBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerNodeUpgradeProtocol.Limit); }
        using var file = ServerNodeUpgradeFiles.CreatePrivateFile(path);
        Span<byte> header = stackalloc byte[HeaderBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(header, magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[sizeof(ulong)..], envelope.Length);
        file.Write(header);
        file.Write(envelope);
        file.Flush(true);
    }

    internal static T Read<T>(string path, ulong magic)
    {
        ServerNodeUpgradeFiles.RequireRegularFile(path);
        using var file = ServerNodeUpgradeFiles.OpenRead(path);
        if (file.Length <= HeaderBytes || file.Length > HeaderBytes + ServerNodeUpgradeProtocol.MaximumReceiptBytes)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        Span<byte> header = stackalloc byte[HeaderBytes];
        file.ReadExactly(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header[sizeof(ulong)..]);
        if (BinaryPrimitives.ReadUInt64LittleEndian(header) != magic || length <= 0
            || length != file.Length - HeaderBytes || length > ServerNodeUpgradeProtocol.MaximumReceiptBytes)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        var encoded = new byte[length];
        file.ReadExactly(encoded);
        return Decode<T>(encoded);
    }

    private static T Decode<T>(byte[] encoded)
    {
        try
        {
            var envelope = NativeSerialization.Deserialize<ServerNodeUpgradeEnvelope>(encoded);
            if (envelope.Payload is null || envelope.Sha256 is not { Length: SHA256.HashSizeInBytes }
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Sha256))
            { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
            return NativeSerialization.Deserialize<T>(envelope.Payload);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.IO.InvalidDataException
            or System.Runtime.Serialization.SerializationException or EndOfStreamException or IndexOutOfRangeException)
        {
            throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt);
        }
    }
}
