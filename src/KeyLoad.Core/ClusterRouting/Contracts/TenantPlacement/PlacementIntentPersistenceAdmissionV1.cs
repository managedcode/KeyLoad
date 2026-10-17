using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PlacementIntentPersistenceAdmissionV1Aliases.PlacementIntentPersistenceAdmissionV1Alias)]
internal sealed record PlacementIntentPersistenceAdmissionV1(
    [property: global::Orleans.Id(0)] PersistPlacementIntentRequest OriginalRequest,
    [property: global::Orleans.Id(1)] ImmutableArray<OwnerCapacitySnapshotV1> OriginalFreshCapacity,
    [property: global::Orleans.Id(2)] string? OriginalCohortFingerprint);
