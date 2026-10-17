namespace KeyLoad.Features.InternalSerialization;

internal static class NativeWireCheck
{
    internal static void Require(bool condition)
    {
        if (!condition)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }
}
