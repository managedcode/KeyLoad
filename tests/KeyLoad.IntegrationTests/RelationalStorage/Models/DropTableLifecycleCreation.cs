namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record DropTableLifecycleCreation(Guid CommandId, ResourceDefinition Definition);
