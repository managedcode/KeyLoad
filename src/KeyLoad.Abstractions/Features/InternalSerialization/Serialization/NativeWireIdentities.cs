namespace KeyLoad.Features.InternalSerialization;

// Native Orleans envelope, collection field and scalar-width identities, not host policy.
internal static class NativeWireIdentities
{
    internal const int EmptyFrameCount = 0;
    internal const int EnvelopeAndValueDepth = 2;
    internal const int NullReferenceId = 0;
    internal const int Fixed32Bytes = sizeof(uint);
    internal const int Fixed64Bytes = sizeof(ulong);
    internal const uint DictionaryComparerId = 0;
    internal const uint DictionaryCountId = 1u;
    internal const uint SequenceCountId = 0u;
    internal const int KeyTypeArgumentIndex = 0;
    internal const int SequenceTypeArgumentIndex = 0;
    internal const int CollectionItemDelta = 1;
    internal const uint DictionaryFieldsPerItem = 2u;
    internal const uint SequenceFieldsPerItem = 1u;
    internal const int FirstElementOrdinal = 1;
    internal const int DictionaryArgumentCount = 2;
    internal const uint FirstFieldId = 0;
    internal const uint EmptyCollectionCount = 0;
    internal const int EmptyElementCount = 0;
    internal const uint MinimumDictionaryEntryBytes = 4u;
    internal const uint MinimumSequenceItemBytes = 2u;
    internal const int VersionFieldDelta = 0;
    internal const int ValueFieldDelta = 1;
    internal const int EmptyRemainingBytes = 0;
}
