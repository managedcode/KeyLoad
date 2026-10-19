namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SchemaVisibleProjectionProtocol
{
    internal const string Reader = "schema-visible-reader";
    internal const string SchemaGrant = "schema.visible";
    internal const string Detail = "The requested field binding is unavailable.";
    internal const string EmptyGrant = "";
    internal const long InitialEpoch = 1;
    internal const long RawEpoch = 2;
    internal const long SchemaEpoch = 3;
    internal const long RevokedEpoch = 4;
    internal const long MixedEpoch = 5;
    internal const long MixedVersion = 2;
    internal const int First = 0;
    internal const int ColdCuts = 2;
    internal const string ValueAlias = "exposed";
    internal const string ProtectedJson = "{\"exposed\":\"CANARY\"}";
    internal const string PublicJson = "{\"exposed\":\"open\"}";
    internal const string IdJson = "{\"exposed\":\"o1\"}";
    internal const string RevisionJson = "{\"exposed\":1}";
    internal const string Star = "*";
    internal const string Scan = "bounded-full-scan";
}
