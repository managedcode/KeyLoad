using System.Security.Cryptography;

namespace KeyLoad.Server.Features.Search;

internal static class TextEnvelopeCodec
{
    internal static byte[] Encode<T>(T value)
    {
        var payload = OrleansSerialization.Serialize(value);
        return OrleansSerialization.Serialize(new TextEnvelope(TextProtocol.FormatVersion,
            payload, SHA256.HashData(payload)));
    }

    internal static T Decode<T>(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var envelope = OrleansSerialization.Deserialize<TextEnvelope>(bytes);
            if (envelope is null || envelope.FormatVersion != TextProtocol.FormatVersion || envelope.Payload is null
                || envelope.Sha256 is null || envelope.Sha256.Length != SHA256.HashSizeInBytes
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Sha256))
            {
                throw KeyLoad.Errors.Fail(KeyLoad.ErrorCode.Corruption, TextProtocol.ProjectionCorrupt);
            }
            return OrleansSerialization.Deserialize<T>(envelope.Payload);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw TextErrors.Corrupt();
        }
    }
}
