namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record RemoteTransferCoordinatorFixtureSelection(string PrincipalId, int? AcceptAttemptCeiling = null, int? RepairCeiling = null);
