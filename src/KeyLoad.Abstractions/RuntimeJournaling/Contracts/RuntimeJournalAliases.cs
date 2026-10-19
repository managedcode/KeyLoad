namespace KeyLoad;

// Immutable infrastructure payload identities; changing CLR names cannot change these aliases.
internal static class RuntimeJournalAliases
{
    internal const string Mutation = "keyload.runtime-journal.mutation";
    internal const string Read = "keyload.runtime-journal.read";
    internal const string Page = "keyload.runtime-journal.page";
    internal const string Catalog = "keyload.runtime-journal.catalog";
    internal const string Snapshot = "keyload.runtime-journal.snapshot";
    internal const string Result = "keyload.runtime-journal.result";
}
