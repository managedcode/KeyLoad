namespace KeyLoad.Orleans;

internal static class ReplicaEnvelopeValidation
{
    internal const int EmptyPayloadLength = 0;

    internal static void Payload(ReadOnlyMemory<byte> payload, int maximumBytes)
    {
        if (payload.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.PayloadExceeded);
        }
    }

    internal static void Discovery(ReadOnlySpan<byte> payload, Guid nonce)
    {
        if (payload.Length > ReplicaTransportProtocol.MaximumDiscoveryBytes || nonce == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidDiscovery);
        }
    }

    internal static bool Address(string address) => !string.IsNullOrWhiteSpace(address)
        && address.Length <= ReplicaTransportProtocol.MaximumAddressCharacters;

    internal static bool Signature(ReadOnlyMemory<byte> signature) => signature.Length == ReplicaTransportProtocol.HashBytes;
}
