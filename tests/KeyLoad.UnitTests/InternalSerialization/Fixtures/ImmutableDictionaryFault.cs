namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal enum ImmutableDictionaryFault
{
    MissingValues, NullValues, DuplicateValues, IncompletePairs, OversizedCount, AlienMember, BaseScope, UnknownComparer, TypedNullComparer, DuplicateComparer, UnknownKeyComparer
}
