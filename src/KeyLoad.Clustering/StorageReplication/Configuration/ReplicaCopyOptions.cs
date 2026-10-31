using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Storage access, independent deployment size and required durable data copies.</summary>
[ConfigurationOptions]
public sealed class ReplicaCopyOptions
{
    private const int DefaultSynchronizationSeconds = 5;
    private static readonly TimeSpan MaximumTimerDuration = TimeSpan.FromMilliseconds(4_294_967_294);
    /// <summary>Root application configuration section.</summary>
    public const string SectionName = "KeyLoad";
    /// <summary>Whether silo owners access separate files or one shared physical store.</summary>
    public StorageAccessMode StorageMode { get; set; } = StorageAccessMode.IsolatedVolumes;
    /// <summary>Number of Orleans silos provisioned by the host.</summary>
    public int SiloCount { get; set; } = 1;
    /// <summary>Number of silo-local stores which must persist an acknowledged operation.</summary>
    public int ReplicaCount { get; set; } = 1;

    /// <summary>Periodic completion of retained canonical work on configured isolated copies.</summary>
    public TimeSpan SynchronizationInterval { get; set; } = TimeSpan.FromSeconds(DefaultSynchronizationSeconds);

    /// <summary>Whether storage access and required copy counts are consistent.</summary>
    public bool IsValid() => Enum.IsDefined(StorageMode)
        && SiloCount >= StorageCopyContract.MinimumCount
        && ReplicaCount >= StorageCopyContract.MinimumCount && ReplicaCount <= SiloCount
        && (StorageMode != StorageAccessMode.SharedVolume || ReplicaCount == StorageCopyContract.MinimumCount)
        && SynchronizationInterval.TotalMilliseconds >= 1 && SynchronizationInterval <= MaximumTimerDuration;

    /// <summary>Rejects invalid storage access and copy counts before opening storage.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(ReplicaCopyOptions),
                [StorageCopyContract.InvalidCounts]);
        }
    }
}
