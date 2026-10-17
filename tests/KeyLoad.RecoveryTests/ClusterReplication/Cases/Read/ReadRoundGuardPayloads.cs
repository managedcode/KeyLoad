namespace KeyLoad.RecoveryTests;

internal static class ReadRoundGuardPayloads
{
    internal const string Nonempty = "nonempty";
    internal const string NullEntries = "null-entries";
    internal const string MissingEntries = "missing-entries";
    internal const string MissingLeader = "missing-leader";
    internal const string MissingTerm = "missing-term";
    internal const string MissingPreviousIndex = "missing-previous-index";
    internal const string MissingPreviousTerm = "missing-previous-term";
    internal const string MissingCommittedIndex = "missing-committed-index";
    internal const string Duplicate = "duplicate";
    internal const string Unknown = "unknown";
    internal const string CaseMutated = "case-mutated-alias";
    internal const string InvalidTerm = "invalid-term";
    internal const string InvalidPrevious = "invalid-previous";
    internal const string Trailing = "trailing";
    internal const string NonemptyString = "nonempty-string";
    internal const string Null = "null-root";
    internal const string Object = "object-root";
    internal const string TrailingString = "trailing-string";
}
