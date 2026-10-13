namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextSelectedSchemaPublicState(NativeTextMaintenanceRf3Scenario Scenario,
    ResourceDefinition Resource, CommandRequest Original, CommitReceipt Receipt,
    TextIndexMaintenanceRequest Build, TextIndexMaintenanceResult Built);
