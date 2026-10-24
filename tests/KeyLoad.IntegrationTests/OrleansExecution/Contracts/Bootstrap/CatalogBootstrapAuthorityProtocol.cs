namespace KeyLoad.IntegrationTests.OrleansExecution;

internal static class CatalogBootstrapAuthorityProtocol
{
    internal const string KeyRevoked = "key-revoked";
    internal const string PrincipalRevoked = "principal-revoked";
    internal const string PrincipalDemoted = "principal-demoted";
    internal const string Root = "root";
    internal const string SystemTenant = "system";
    internal const string Wildcard = "*";
    internal const string Collection = "bootstrap-authority";
    internal const string Database = "database";
    internal const string Document = "retained";
    internal const string OriginalJson = "{\"value\":1,\"scope\":\"catalog-restart\"}";
    internal const string HealthyJson = "{\"value\":2,\"scope\":\"catalog-restart\"}";
    internal const string Put = "putDocument";
    internal const string Unavailable = "The credential is unavailable or expired.";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string AdministrationDenied = "Cluster administration is required.";
    internal const string McpDenied = "The authenticated principal is not permitted to perform this operation.";
    internal const string GuidFormat = "N";
    internal const string TenantPrefix = "catalog-";
    internal const string AdministratorPrefix = "survivor-";
    internal const string ReaderPrefix = "reader-";
    internal const string SecretSeparator = ".";
    internal const int FirstIndex = 0;
    internal const int SecondIndex = 1;
    internal const int EmptyCount = 0;
    internal const long EmptyPosition = 0;
    internal const int Version = 1;
    internal const int ColdCycles = 2;
    internal const long OriginalRevision = 1;
    internal const long HealthyRevision = 2;
    internal const long ChangedEpoch = 2;
    internal const long EmptyRevision = 0;
    internal static readonly string[] Nodes = ["node1", "node2", "node3"];
}
