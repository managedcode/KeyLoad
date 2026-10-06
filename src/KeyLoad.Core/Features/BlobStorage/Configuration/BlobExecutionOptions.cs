namespace KeyLoad.Core;

/// <summary>Centrally bound work limits for blob catalog proof and offline restore.</summary>
[ConfigurationOptions]
public sealed record BlobExecutionOptions
{
    /// <summary>The feature's native configuration section.</summary>
    public const string SectionName = "KeyLoad:BlobExecution";
    /// <summary>The startup rejection for invalid blob work limits.</summary>
    public const string ValidationMessage = "The blob restore and catalog proof work limits are invalid.";
    private const int MinimumWorkCount = 1;
    private const int DefaultMetadataPageSize = 128;
    private const int DefaultInitialCatalogProofRecords = 10_000;
    private const long DefaultRestorePageBytes = 4_194_304;

    /// <summary>The maximum metadata records retained in one restore page.</summary>
    public int MetadataPageSize { get; init; } = DefaultMetadataPageSize;
    /// <summary>The maximum catalog records examined by the initial empty-store proof.</summary>
    public int InitialCatalogProofRecords { get; init; } = DefaultInitialCatalogProofRecords;
    /// <summary>The maximum key/value bytes retained by one restore page.</summary>
    public long RestorePageBytes { get; init; } = DefaultRestorePageBytes;

    /// <summary>Checks positive work budgets against their original inclusive ceilings.</summary>
    public bool IsValid() => MetadataPageSize is >= MinimumWorkCount and <= DefaultMetadataPageSize
        && InitialCatalogProofRecords is >= MinimumWorkCount and <= DefaultInitialCatalogProofRecords
        && RestorePageBytes is >= MinimumWorkCount and <= DefaultRestorePageBytes;

    /// <summary>Rejects invalid settings before operation admission.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new InvalidOperationException(ValidationMessage); }
    }
}
