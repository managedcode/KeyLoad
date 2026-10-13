namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextReleasedRepairPublicState(NativeTextSelectedSchemaPublicState Original,
    TextIndexMaintenanceRequest Release, TextIndexMaintenanceResult Released,
    NativeTextSelectedSchemaPublicState Current);
