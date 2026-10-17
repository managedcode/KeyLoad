namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record PhysicalOwnerRegistrationRf3State(int NodeOrdinal, bool Present,
    bool OwnedResourceMatches, PhysicalOwnerRegistrationRf3ResourceState State);
