namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextEmptyMaintainedPublicState(NativeTextMaintenanceRf3Scenario Scenario,
    ResourceDefinition Resource, TextIndexMaintenanceRequest Build, TextIndexMaintenanceResult Built);
