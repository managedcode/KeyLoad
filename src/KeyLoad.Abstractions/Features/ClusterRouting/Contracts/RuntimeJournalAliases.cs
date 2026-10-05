namespace KeyLoad;

// Immutable infrastructure payload identities; changing CLR names cannot change these aliases.
internal static class RuntimeJournalAliases
{
    internal const string Mutation = "keyload.runtime-journal.mutation.v1";
    internal const string Read = "keyload.runtime-journal.read.v1";
    internal const string Page = "keyload.runtime-journal.page.v1";
    internal const string Catalog = "keyload.runtime-journal.catalog.v1";
    internal const string Snapshot = "keyload.runtime-journal.snapshot.v1";
    internal const string Result = "keyload.runtime-journal.result.v1";
}
