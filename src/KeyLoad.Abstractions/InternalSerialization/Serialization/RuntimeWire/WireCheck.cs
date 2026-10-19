namespace KeyLoad.Features.InternalSerialization;

internal static class WireCheck
{
    internal static void Require(bool condition)
    {
        if (!condition)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
    }
}
