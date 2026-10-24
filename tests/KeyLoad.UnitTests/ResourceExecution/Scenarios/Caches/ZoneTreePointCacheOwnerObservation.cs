using KeyLoad.Storage.ZoneTree.ResourceExecution;

namespace KeyLoad.UnitTests.ResourceExecution;

internal readonly record struct ZoneTreePointCacheOwnerObservation(
    ZoneTreePointCacheOwnerStatus Status, ZoneTreePointCacheOwnerIdentity Identity);
