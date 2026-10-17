namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal enum NativeEnvelopeFault
{
    WrongRoot, MissingVersion, MissingValue, DuplicateVersion, DuplicateValue, UnknownField
}
