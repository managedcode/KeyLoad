namespace KeyLoad;

/// <summary>Bounds file reservations, retained provider objects and metadata per resource.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(FileStoragePolicy.ContractAlias)]
public sealed record FileStoragePolicy
{
    internal const string ContractAlias = "keyload.file.policy.v1";
    private const int MinimumCapacity = 1;
    private const long DefaultMaximumFileBytes = 274_877_906_944;
    private const long DefaultMaximumStoredBytes = 1_099_511_627_776;
    private const int DefaultMaximumFiles = 100_000;
    private const int DefaultMaximumUploads = 128;
    private const int DefaultMaximumObjects = 1_000_000;

    /// <summary>Maximum bytes in one upload.</summary>
    [Orleans.Id(0)] public long MaximumFileBytes { get; init; } = DefaultMaximumFileBytes;
    /// <summary>Maximum reserved and retained physical bytes.</summary>
    [Orleans.Id(1)] public long MaximumStoredBytes { get; init; } = DefaultMaximumStoredBytes;
    /// <summary>Maximum visible files.</summary>
    [Orleans.Id(2)] public int MaximumFiles { get; init; } = DefaultMaximumFiles;
    /// <summary>Maximum incomplete uploads.</summary>
    [Orleans.Id(3)] public int MaximumUploads { get; init; } = DefaultMaximumUploads;
    /// <summary>Maximum retained physical objects, including reservations.</summary>
    [Orleans.Id(4)] public int MaximumObjects { get; init; } = DefaultMaximumObjects;

    /// <summary>Rejects invalid resource budgets before effects.</summary>
    public bool IsValid() => MaximumFileBytes >= MinimumCapacity && MaximumStoredBytes >= MaximumFileBytes
        && MaximumFiles >= MinimumCapacity && MaximumUploads >= MinimumCapacity && MaximumObjects >= MaximumFiles;
}
