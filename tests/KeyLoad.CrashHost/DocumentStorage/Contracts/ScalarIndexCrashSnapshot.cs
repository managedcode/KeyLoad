namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal sealed record ScalarIndexDocumentState(string TenantId, string DatabaseId, string TransactionDomainId,
    string PartitionKey, string Collection, string Id, string? Json, long Revision, bool Deleted);
internal sealed record ScalarIndexMembership(string PartitionKey, string Value, string[] DocumentIds);
internal sealed record ScalarIndexCrashSnapshot(ScalarIndexDocumentState[] Documents, ScalarIndexMembership[] Memberships);
