namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class BinderFieldLineageProtocol
{
    internal const int None = 0;
    internal const int ReadOnly = 1;
    internal const int UseOnly = 2;
    internal const int Both = ReadOnly | UseOnly;
    internal const int ProjectionOnly = 0;
    internal const int PredicateShape = 1;
    internal const int SortShape = 2;
    internal const long InitialEpoch = 1;
    internal const long InitialOwnershipEpoch = 1;
    internal const long OriginalRevision = 1;
    internal const string Collection = "orders";
    internal const string DocumentId = "o1";
    internal const string Tenant = "tenant";
    internal const string Database = "database";
    internal const string Root = "root";
    internal const string ReaderPrefix = "lineage-";
    internal const string ChangingSubject = "lineage-changing";
    internal const string RawRead = "pii.read";
    internal const string RawUse = "pii.use";
    internal const string Classification = "pii";
    internal const string ArrayPolicy = "/people/*/secret";
    internal const string DottedPolicy = "/odd.name";
    internal const string StatusPath = "/status";
    internal const string ArrayElementPath = "/people/0/secret";
    internal const string PublicAlias = "public_value";
    internal const string ProtectedLookingAlias = "odd.name";
    internal const string NestedAlias = "nested_value";
    internal const string Canary = "CANARY";
    internal const string PutDocumentKind = "putDocument";
    internal const string ScanPath = "bounded-full-scan";
    internal const string UseDenied = "The query requires a protected field-use grant.";
    internal const string UnsupportedDetail = "The SQL statement or execution clause is unsupported.";
}
