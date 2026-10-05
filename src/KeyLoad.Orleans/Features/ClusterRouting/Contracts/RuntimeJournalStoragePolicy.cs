namespace KeyLoad.Orleans;

internal static class RuntimeJournalStoragePolicy
{
    internal const string BinaryFormat = "orleans-binary";
    internal const string InvalidLimits = "Runtime journal capacity cannot fit the configured native request budget.";
    internal const string InvalidReply = "The native runtime journal returned an invalid response.";
    internal const string RequestFailed = "The native runtime journal request could not complete.";
    internal const string Inconsistent = "The runtime journal changed after this storage handle captured it.";
    internal const string MetadataConflict = "The runtime journal metadata no longer matches the expected ETag.";
    internal const string InvalidPage = "The native runtime journal returned an invalid bounded page.";
    internal const string InvalidCatalog = "The native runtime journal returned an invalid catalog entry.";
    internal const string InvalidMetadata = "The native runtime journal metadata exceeds its admitted bounds.";
}
