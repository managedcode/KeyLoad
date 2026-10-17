namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class DocumentSessionReadRf3Protocol
{
    internal const string Collection = "session-documents";
    internal const string DocumentId = "one";
    internal const string Tenant = "session-tenant";
    internal const string Database = "session-database";
    internal const string FirstJson = "{\"value\":\"acknowledged\"}";
    internal const string SecondJson = "{\"value\":\"continued\"}";
    internal const string KillScenario = "document-session-token-failover";
    internal const int First = 1;
    internal const int Second = 2;
    internal const int Zero = 0;
    internal const string WrongIncarnation = "The document session token belongs to another incarnation.";
    internal const string OutOfScope = "The document session token belongs to another atomic partition or placement.";
    internal const string Future = "The document session token is beyond the current quorum-applied cut.";
    internal const string Invalid = "The document session token position must be positive.";
}
