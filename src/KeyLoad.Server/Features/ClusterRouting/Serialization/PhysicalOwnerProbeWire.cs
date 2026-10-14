namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalOwnerProbeWire
{
    private const int MinimumBodyBytes = 1;
    internal static byte[] Encode<T>(T value)
    {
        var bytes = NativeSerialization.Measure(value);
        if (bytes is < MinimumBodyBytes or > PhysicalOwnerProbeProtocol.MaximumBodyBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, PhysicalOwnerProbeProtocol.InvalidProof);
        }
        return NativeSerialization.Serialize(value);
    }
    internal static PhysicalOwnerProbeCallV1 DecodeCall(ReadOnlySpan<byte> body)
    {
        if (body.Length is < MinimumBodyBytes or > PhysicalOwnerProbeProtocol.MaximumBodyBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, PhysicalOwnerProbeProtocol.InvalidProof);
        }
        try
        {
            return NativeSerialization.Deserialize<PhysicalOwnerProbeCallV1>(body);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Validation, PhysicalOwnerProbeProtocol.InvalidProof);
        }
    }
}
