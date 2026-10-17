using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Reads complete canonical model families while keeping original outcome authority distinct.</summary>
internal static class ControlledPartitionMovementTargetModelImage
{
    internal static string[] Read(ControlledPartitionMovementNode target)
    {
        var original = ControlledPartitionMovementRawImage.Bytes(target.Store);
        return PartitionRecordFamilies.All.Where(family => family is not (PartitionRecordFamilies.OutcomeV2
                or PartitionRecordFamilies.OutcomeLocator or PartitionRecordFamilies.OutcomeLocatorV2))
            .SelectMany(family =>
            {
                var prefix = Convert.ToHexString(KeySpace.Partition(family, ControlledPartitionMovementCorpus.Partition));
                return original.Where(row => row.StartsWith(prefix, StringComparison.Ordinal));
            }).ToArray();
    }
}
