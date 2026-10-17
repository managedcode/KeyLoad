using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record ControlledPartitionMovementRetainedOutcome(long Index, ReplicatedOperation Operation,
    CommandOutcomeScopeKind Scope, byte[] Key, byte[]? LocatorKey, byte[] Bytes, StoredOutcome Outcome);
