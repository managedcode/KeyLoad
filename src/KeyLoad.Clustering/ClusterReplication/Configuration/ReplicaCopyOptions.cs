using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Independent deployment size and required durable data copies.</summary>
[ConfigurationOptions]
public sealed class ReplicaCopyOptions
{
    /// <summary>Root application configuration section.</summary>
    public const string SectionName = "KeyLoad";
    /// <summary>Number of Orleans silos provisioned by the host.</summary>
    public int SiloCount { get; set; } = 1;
    /// <summary>Number of silo-local stores which must persist an acknowledged operation.</summary>
    public int ReplicaCount { get; set; } = 1;

    /// <summary>Rejects impossible copy counts before opening storage.</summary>
    public void Validate()
    {
        if (SiloCount < StorageCopyContract.MinimumCount || ReplicaCount < StorageCopyContract.MinimumCount || ReplicaCount > SiloCount)
        {
            throw new OptionsValidationException(SectionName, typeof(ReplicaCopyOptions),
                [StorageCopyContract.InvalidCounts]);
        }
    }
}
