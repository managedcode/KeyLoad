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
    internal const string RequiredJournalIdentifier = "A runtime journal identifier is required.";
    internal const string ConsumerLeftUnreadData = "The journal storage consumer did not read all supplied journal data.";
    internal const int InitialMetadataByteCount = 0;
    internal const int InitialOwnerGeneration = 0;
    internal const int InitialContentRevision = 0;
    internal const long InitialReadOffset = 0;
    internal const int NoBufferedBytes = 0;
    internal const int MissingCharacterIndex = -1;
    internal const int AvailableHandleGatePermits = 1;
    internal const int MaximumHandleGatePermits = 1;
    internal const int InitialGateIndex = 0;
    internal const int NoDisposedProvider = 0;
    internal const int DisposedProvider = 1;
    internal const int EqualOrdinalComparison = 0;
    internal const int NoUncertaintyRetryCount = 0;
    internal const char ProviderOwnedMetadataPrefix = '$';
    internal const char InvalidMetadataKeyCharacter = '\0';
}
