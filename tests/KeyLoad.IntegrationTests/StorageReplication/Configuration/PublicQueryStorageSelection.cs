using System.Globalization;
using KeyLoad.Clustering;

namespace KeyLoad.IntegrationTests.Features.StorageReplication;

/// <summary>Selects physical storage only for the keyed public-query SDK and MCP cases.</summary>
internal readonly record struct PublicQueryStorageSelection(StorageAccessMode Mode, int ReplicaCount)
{
    internal const string Key = "public-query-storage";
    private const string ModeSetting = "KeyLoadTests__PublicQuery__StorageMode";
    private const string CopiesSetting = "KeyLoadTests__PublicQuery__ReplicaCount";
    private const string InvalidSelection = "Public query storage selection requires an exact storage mode and an invariant copy count together.";

    internal static PublicQueryStorageSelection Read(string key)
    {
        var standard = new PublicQueryStorageSelection(StorageAccessMode.IsolatedVolumes, ClusterFixtureProtocol.NodeCount);
        if (!string.Equals(key, Key, StringComparison.Ordinal))
        {
            return standard;
        }
        var configuredMode = Environment.GetEnvironmentVariable(ModeSetting);
        var configuredCopies = Environment.GetEnvironmentVariable(CopiesSetting);
        if (configuredMode is null && configuredCopies is null)
        {
            return standard;
        }
        if (!Enum.TryParse<StorageAccessMode>(configuredMode, ignoreCase: false, out var mode)
            || !string.Equals(configuredMode, mode.ToString(), StringComparison.Ordinal)
            || !int.TryParse(configuredCopies, NumberStyles.None, CultureInfo.InvariantCulture, out var copies))
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        new ReplicaCopyOptions
        {
            StorageMode = mode,
            SiloCount = ClusterFixtureProtocol.NodeCount,
            ReplicaCount = copies
        }.Validate();
        return new(mode, copies);
    }
}
