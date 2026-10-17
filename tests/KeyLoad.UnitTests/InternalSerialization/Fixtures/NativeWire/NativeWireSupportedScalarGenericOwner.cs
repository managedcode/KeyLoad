namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativeWireSupportedScalarGenericOwner<T>
{
    internal enum State : byte
    {
        Empty,
        Populated
    }
}
