namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresClusterProtocol
{
    internal const string Endpoint = "execution-sql";
    internal const string DatabaseAlias = "keyload";
    internal const string Application = "keyload-execution-session-cluster";
    internal const string ResultColumn = "keyload_result";
    internal const string JsonbType = "jsonb";
    internal const string Request = "request";
    internal const string CommitSql = "EXEC keyload_documents_commit $1";
    internal const string InvalidResult = "The execution SQL result is incomplete.";
    internal const int OneColumn = 1;
    internal const int FirstColumn = 0;
    internal const string Tenant = "sql-execution";
    internal const string Database = "interop";
    internal const string Domain = "atomic";
    internal const string PartitionKey = "one";
    internal static PartitionRef Partition => new(Tenant, Database, Domain, PartitionKey);
    internal const int ScalarDialect = 1;
    internal const int JoinDialect = 2;
    internal const long SeedRevision = 1;
    internal const string PrincipalPrefix = "c1-probe-execution-sql-";
    internal const string CredentialPrefix = "execution-sql-key-";
    internal const string UnauthenticatedSqlState = "28P01";
    internal const string PermissionSqlState = "42501";
    internal const string ConflictSqlState = "40001";
    internal const string UnsupportedSqlState = "0A000";
    internal const string CancellationSqlState = "57014";
}
