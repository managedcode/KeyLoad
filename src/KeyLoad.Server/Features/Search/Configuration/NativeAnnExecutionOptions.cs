namespace KeyLoad.Server.Features.Search;

/// <summary>Finite native generation ownership, files and resident reservation admission.</summary>
[ConfigurationOptions]
internal sealed record NativeAnnExecutionOptions
{
    /// <summary>Central native ANN owner configuration section.</summary>
    public const string SectionName = "KeyLoad:NativeAnn";
    /// <summary>Safe invalid configuration detail.</summary>
    public const string ValidationMessage = "The native ANN owner bounds are invalid.";
    private const int DefaultGenerations = 8;
    private const int MaximumGenerations = 64;
    private const int DefaultBuffer = 65_536;
    private const int DefaultManifestBytes = 65_536;
    private const long DefaultDiskBytes = 536_870_912;
    private const long DefaultResidentBytes = 536_870_912;
    private const long MaximumBytes = 8_589_934_592;
    private const int MinimumBytes = 1_024;
    private const int MinimumCount = 1;

    /// <summary>Maximum published and unpublished immutable generation directories.</summary>
    public int MaximumOwnedGenerations { get; init; } = DefaultGenerations;
    /// <summary>Native file buffering; this does not widen an operation deadline.</summary>
    public int FileBufferBytes { get; init; } = DefaultBuffer;
    /// <summary>Maximum individual generated manifest or owner receipt bytes.</summary>
    public int MaximumManifestBytes { get; init; } = DefaultManifestBytes;
    /// <summary>Maximum complete owner file bytes across all generations.</summary>
    public long MaximumDiskBytes { get; init; } = DefaultDiskBytes;
    /// <summary>Maximum simultaneously resident seeds, indexes and staged construction reservations.</summary>
    public long MaximumResidentBytes { get; init; } = DefaultResidentBytes;

    /// <summary>Checks all finite ownership bounds.</summary>
    public bool IsValid() => MaximumOwnedGenerations is >= MinimumCount and <= MaximumGenerations
        && FileBufferBytes is >= MinimumBytes and <= DefaultBuffer
        && MaximumManifestBytes is >= MinimumBytes and <= DefaultManifestBytes
        && MaximumDiskBytes is >= MinimumBytes and <= MaximumBytes
        && MaximumResidentBytes is >= MinimumBytes and <= MaximumBytes;

    /// <summary>Rejects invalid owner settings before ownership.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw Errors.Fail(ErrorCode.Validation, ValidationMessage); }
    }
}
