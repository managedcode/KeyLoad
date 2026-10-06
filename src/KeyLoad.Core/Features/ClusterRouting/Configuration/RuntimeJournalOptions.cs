namespace KeyLoad.Core;

/// <summary>Central admission policy for the private replicated native runtime journal.</summary>
[ConfigurationOptions]
public sealed record RuntimeJournalOptions
{
    /// <summary>The configuration section owned by server composition.</summary>
    public const string SectionName = "KeyLoad:RuntimeJournal";
    /// <summary>The safe startup validation failure.</summary>
    public const string ValidationMessage = "Runtime journal limits exceed the bounded native recovery contract.";
    private const int MinimumJournalCount = 1;
    private const int MinimumJournalLength = 1;
    private const long MinimumCatalogLength = 1;
    private const int MinimumChunkLength = 1;
    private const int MinimumMetadataLength = 1;
    private const int MinimumNameLength = 1;
    private const int MinimumMetadataEntryCount = 1;
    private const int NoUncertaintyRetries = 0;
    private const int MaximumUncertaintyRetries = 1;
    private const int MaximumJournalCount = 32;
    private const int MaximumJournalLength = 2_097_152;
    private const long MaximumTotalLength = 67_108_864;
    private const int MaximumChunkLength = 65_536;
    private const int MaximumMetadataLength = 8_192;
    private const int MaximumNameLength = 512;
    private const int MaximumMetadataEntryCount = 64;

    /// <summary>Gets the explicit parent directory for verified backups of existing unmarked node stores.</summary>
    public string? ReaderUpgradeBackupDirectory { get; init; }
    /// <summary>Gets or sets the maximum retained journals, including empty journals.</summary>
    public int MaximumJournals { get; init; } = MaximumJournalCount;
    /// <summary>Gets or sets the maximum opaque bytes in one journal.</summary>
    public int MaximumJournalBytes { get; init; } = MaximumJournalLength;
    /// <summary>Gets or sets the maximum admitted opaque bytes across the catalog.</summary>
    public long MaximumTotalBytes { get; init; } = MaximumTotalLength;
    /// <summary>Gets or sets the maximum canonical chunk and read-page length.</summary>
    public int ChunkBytes { get; init; } = MaximumChunkLength;
    /// <summary>Gets or sets the total UTF8 metadata key/value budget per journal.</summary>
    public int MaximumMetadataBytes { get; init; } = MaximumMetadataLength;
    /// <summary>Gets or sets the UTF8 name, metadata key and individual value limit.</summary>
    public int MaximumNameBytes { get; init; } = MaximumNameLength;
    /// <summary>Gets or sets the maximum native metadata properties.</summary>
    public int MaximumMetadataEntries { get; init; } = MaximumMetadataEntryCount;
    /// <summary>Gets the bounded count of retries using the same durable command identity.</summary>
    public int UncertaintyRetryCount { get; init; } = 1;
    /// <summary>Gets or sets whether native journaling replaces snapshots to permit shrinkage at quota.</summary>
    public bool RequestCompaction { get; init; } = true;

    /// <summary>Checks that every policy remains inside the native recovery bounds.</summary>
    /// <returns>Whether the configuration is admissible.</returns>
    public bool IsValid()
        => (ReaderUpgradeBackupDirectory is null || Path.IsPathFullyQualified(ReaderUpgradeBackupDirectory))
            && MaximumJournals is >= MinimumJournalCount and <= MaximumJournalCount
            && MaximumJournalBytes is >= MinimumJournalLength and <= MaximumJournalLength
            && MaximumTotalBytes is >= MinimumCatalogLength and <= MaximumTotalLength
            && ChunkBytes is >= MinimumChunkLength and <= MaximumChunkLength
            && ChunkBytes <= MaximumJournalBytes && MaximumJournalBytes <= MaximumTotalBytes
            && MaximumMetadataBytes is >= MinimumMetadataLength and <= MaximumMetadataLength
            && MaximumNameBytes is >= MinimumNameLength and <= MaximumNameLength
            && MaximumMetadataEntries is >= MinimumMetadataEntryCount and <= MaximumMetadataEntryCount
            && UncertaintyRetryCount is >= NoUncertaintyRetries and <= MaximumUncertaintyRetries;
}
