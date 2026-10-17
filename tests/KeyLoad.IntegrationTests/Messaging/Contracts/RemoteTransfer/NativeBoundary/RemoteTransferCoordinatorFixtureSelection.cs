namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record RemoteTransferCoordinatorFixtureSelection(string PrincipalId, int? AcceptAttemptCeiling = null, int? RepairCeiling = null);
