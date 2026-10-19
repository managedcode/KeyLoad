using System.Security.Cryptography;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextEnvelopeCodec
{
    internal static byte[] Encode<T>(T value)
    {
        var payload = NativeSerialization.Serialize(value);
        return NativeSerialization.Serialize(new NativeTextEnvelope(NativeTextProtocol.FormatVersion,
            payload, SHA256.HashData(payload)));
    }

    internal static T Decode<T>(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var envelope = NativeSerialization.Deserialize<NativeTextEnvelope>(bytes);
            if (envelope is null || envelope.FormatVersion != NativeTextProtocol.FormatVersion || envelope.Payload is null
                || envelope.Sha256 is null || envelope.Sha256.Length != SHA256.HashSizeInBytes
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Sha256))
            {
                throw KeyLoad.Errors.Fail(KeyLoad.ErrorCode.Corruption, NativeTextProtocol.ProjectionCorrupt);
            }
            return NativeSerialization.Deserialize<T>(envelope.Payload);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw NativeTextErrors.Corrupt();
        }
    }
}
