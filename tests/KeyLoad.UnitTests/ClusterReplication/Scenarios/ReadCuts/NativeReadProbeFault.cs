namespace KeyLoad.UnitTests;

internal enum NativeReadProbeFault
{
    None, MissingTerm, MissingPreviousIndex, MissingPreviousTerm, MissingCommittedIndex,
    ZeroTerm, NegativePreviousIndex, ConflictingPreviousTerm, NegativeCommittedIndex,
    NullEntries, DuplicateEntries, UnknownEntries, MissingEntries, TrailingObject
}
