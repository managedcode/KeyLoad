namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryLifetimeNativeProtocol
{
    internal const long SourceEpoch = 5;
    internal const long TargetEpoch = 8;
    internal const long NextEpoch = 1;
    internal const int Version = 1;
    internal const int Limit = 1;
    internal const int GrantDivisor = 4;
    internal const int NoRows = 0;
    internal const int ColdCount = 2;
    internal const string Text = "destination";
    internal const string EqualsOperator = "=";
    internal const string PrivateValue = "private-canary";
    internal const string PrivateTerm = "private";
    internal const string NullSecretJson = """{"secret":null}""";
    internal const string Star = "*";
    internal const string SecretAlias = "secret";
    internal const string MissingPath = "/missing";
    internal const string OtherResource = "independent-resource";
    internal const string SchemaGrant = "schema.secret";
    internal const string SchemaDenied = "The requested field binding is unavailable.";
    internal const string UseDenied = "The query requires a protected field-use grant.";
    internal const string MissingFailure = "The original V2 read failure is missing.";
}
