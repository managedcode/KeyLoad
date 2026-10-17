namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal enum NativeImmutableDictionaryFault
{
    MissingValues, NullValues, DuplicateValues, IncompletePairs, OversizedCount, AlienMember, BaseScope, UnknownComparer, TypedNullComparer, DuplicateComparer, UnknownKeyComparer
}
