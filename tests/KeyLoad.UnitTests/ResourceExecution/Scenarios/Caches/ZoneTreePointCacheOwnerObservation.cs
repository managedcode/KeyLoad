using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal readonly record struct ZoneTreePointCacheOwnerObservation(
    ZoneTreePointCacheOwnerStatus Status, ZoneTreePointCacheOwnerIdentity Identity);
