namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record TenantCapacityPlacementRf3OccupiedHealthyState(
    CommandRequest Primary, CommitReceipt PrimaryReceipt,
    CommandRequest Sibling, CommitReceipt SiblingReceipt);
