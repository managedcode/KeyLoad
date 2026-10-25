using KeyLoad.Storage.ZoneTree.ResourceExecution;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal readonly record struct ZoneTreePointCacheOwnerObservation(
    ZoneTreePointCacheOwnerStatus Status, ZoneTreePointCacheOwnerIdentity Identity);
