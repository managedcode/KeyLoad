namespace KeyLoad.FunctionalTests.Authorization;

internal sealed record SafeProjectorOperationState(ResourceDefinition Resource, PrincipalRecord Reader,
    SafeProjectorOperationCommand ResourceCommand, SafeProjectorOperationCommand PrincipalCommand,
    SafeProjectorOperationCommand Seed);
