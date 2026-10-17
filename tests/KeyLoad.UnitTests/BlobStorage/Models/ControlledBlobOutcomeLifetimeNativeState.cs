using System.Collections.Immutable;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record ControlledBlobOutcomeLifetimeNativeState(ControlledPartitionMovementNode Source,
    ControlledPartitionMovementNode Target, ImmutableArray<ControlledBlobOutcomeLifetimeNativeProof> Proofs,
    RegisteredPhysicalOwnerV1 Destination, string CallerAddress);
