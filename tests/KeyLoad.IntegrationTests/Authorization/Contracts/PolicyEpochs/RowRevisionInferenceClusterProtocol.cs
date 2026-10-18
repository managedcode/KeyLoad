namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceClusterProtocol
{
    internal const string Root = "root";
    internal const string TenantPrefix = "revision-";
    internal const string ReaderPrefix = "revision-reader-";
    internal const string Domain = "revision-domain";
    internal const string SafeDenied = "The authenticated principal is not permitted to perform this operation.";
    internal const string SafeOther = "The database operation could not be completed.";
    internal const string PatchedJson = "{\"secret\":\"ROW-REVISION-PRIVATE-CANARY\",\"value\":2}";
    internal const string Canary = "ROW-REVISION-PRIVATE-CANARY";
    internal const string HiddenJson = "{\"value\":1,\"secret\":\"ROW-REVISION-PRIVATE-CANARY\"}";
    internal const int InitialRecords = 2;
    internal const int RepairRecords = 3;
    internal const int HealthyRecords = 4;
    internal const int First = 0;
    internal const string Reader = "revision-reader";
    internal const string Foreign = "revision-foreign";
    internal const string Tenant = "tenant";
    internal const string Database = "database";
    internal const string Collection = "revision-private-rows";
    internal const string Hidden = "hidden";
    internal const string Own = "own";
    internal const string Missing = "missing";
    internal const string Tombstone = "tombstone";
    internal const string Original = "{\"value\":1}";
    internal const string Changed = "{\"value\":2}";
    internal const string ValuePath = "/value";
    internal const string ValueJson = "2";
    internal const string Put = "put";
    internal const string Patch = "patch";
    internal const string Delete = "delete";
    internal const string PutReceipt = "putDocument";
    internal const string PatchReceipt = "patchDocument";
    internal const string DeleteReceipt = "deleteDocument";
    internal const string RowDenied = "The principal cannot write this row scope.";
    internal const string RevisionDenied = "The expected revision does not match.";
    internal const string Unavailable = "The document is unavailable.";
    internal const string CredentialSeparator = ".";
    internal const long WrongRevision = 0;
    internal const long OriginalRevision = 1;
    internal const long RepairRevision = 2;
    internal const long HealthyRevision = 3;
    internal const long PolicyEpoch = 1;
    internal const int ColdCycles = 2;
}
