namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class WireSupportedScalarGenericOwner<T>
{
    internal enum State : byte
    {
        Empty,
        Populated
    }
}
