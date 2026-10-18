namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class SharedDomainPublicProtocol
{
    internal const string TenantPrefix = "cluster-";
    internal const string ColdScenario = "shared-transaction-domains";
    internal const string PutKind = "putDocument";
    internal const string AppendKind = "appendEvents";
    internal const string EnqueueKind = "enqueue";
    internal const string MissingReceipt = "The original shared-domain public receipt is absent.";
    internal const string UnexpectedMutation = "The shared-domain public producer contains an unexpected mutation.";
    internal const string TokenScopeDetail = "The delivery token scope is invalid.";
    internal const string EpochDetail = "The principal policy changed since this command was evaluated.";
    internal const int NoAttempts = 0;
    internal const int FirstAttempt = 1;
    internal const int ReceiveCount = 1;
    internal const int ReceiveBytes = 4096;
    internal const int LeaseSeconds = 30;
    internal const int EventPageLimit = 4;
    internal const int ProducerEffects = 3;
    internal const long RevokedEpoch = 2L;
    internal const long RestoredEpoch = 3L;
}
