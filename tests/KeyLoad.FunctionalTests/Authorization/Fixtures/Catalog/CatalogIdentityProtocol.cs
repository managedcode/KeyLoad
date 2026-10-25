using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class CatalogIdentityProtocol
{
    internal const string Reader = "identity-reader";
    internal const string Root = "root";
    internal const string Collection = "identity-records";
    internal const string Row = "record";
    internal const string Key = "identity-key";
    internal const string Secret = "identity-key.execution-owned-matrix-credential";
    internal const string WrongSecret = "identity-key.execution-owned-wrong-credential";
    internal const string Original = "{\"public\":\"open\",\"secret\":\"CANARY\"}";
    internal const string Current = "{\"public\":\"closed\",\"secret\":\"CANARY\"}";
    internal const string Hidden = "{\"public\":\"open\"}";
    internal const string Use = "identity.use";
    internal const string Read = "identity.read";
    internal const string Unavailable = "The credential is unavailable or expired.";
    internal static PrincipalRecord Principal => new(Reader, "tenant",
        [new("database", Collection, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query)], []);
    internal static AstQueryRequest Request(PartitionRef partition, bool filter = false)
        => new(partition, new(Collection, null, [new("*", "*")], filter
            ? new Comparison(new FieldOperand("/secret"), "=", ValueOperand.Create("CANARY")) : null,
            [new("/@id", false)], 10), AllowFullScan: true);
    internal static QueryRequest Sql(PartitionRef partition)
        => new(partition, "SELECT TOP (10) * FROM [identity-records] ORDER BY id", AllowFullScan: true);
}
