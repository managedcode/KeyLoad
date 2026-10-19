namespace KeyLoad.IntegrationTests.Features.StorageReplication;

internal static class StrongReadSessionProtocol
{
    internal const string Collection = "strong-session-absence";
    internal const string TenantPrefix = "strong-session-";
    internal const string Database = "database";
    internal const string Domain = "domain";
    internal const string SentinelId = "sentinel";
    internal const string TargetId = "target";
    internal const string AbsentId = "never-created";
    internal const string SentinelJson = """{"kind":"sentinel"}""";
    internal const string TargetJson = """{"kind":"created"}""";
    internal const string PutKind = "putDocument";
    internal const string DeleteKind = "deleteDocument";
    internal const string DocumentField = "document";
    internal const string KillScenario = "strong-session-absence-failover";
    internal const string MissingState = "The original acknowledged session state is unavailable.";
    internal const string WrongIncarnation = "The document session token belongs to another incarnation.";
    internal const string WrongScope = "The document session token belongs to another atomic partition or placement.";
    internal const string InvalidPosition = "The document session token position must be positive.";
    internal const string FuturePosition = "The document session token is beyond the current committed storage cut.";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string Star = "*";
    internal const string IdPath = "/@id";
    internal const string Scan = "bounded-full-scan";
    internal const int InventoryBound = 8;
    internal const int ColdCuts = 2;
    internal const int AuthorityPrecedenceVariants = 1;
    internal const long CreatedRevision = 1;
    internal const long DeletedRevision = 2;
    internal const long InvalidCut = 0;
    internal const long DeniedEpoch = 2;
    internal const long RepairedEpoch = 3;
}
