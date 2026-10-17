using System.Collections.Immutable;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Six independent local native stores; no consensus or remote-voter qualification is represented.</summary>
internal sealed record ResourceRetirementPreparationValidationState(
    IReadOnlyList<ControlledPartitionMovementNode> Nodes,
    ResourceRetirementPreparationStateV1 Preparation,
    ImmutableArray<ResourceRetirementOwnerStatusV1> Statuses);
