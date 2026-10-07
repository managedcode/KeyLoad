namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal sealed record CompositeIndexDocumentState(string TenantId, string DatabaseId, string TransactionDomainId,
    string PartitionKey, string Collection, string Id, string? Json, long Revision, bool Deleted);
internal sealed record CompositeIndexMembership(string PartitionKey, string Value, string[] DocumentIds);
internal sealed record CompositeIndexCrashSnapshot(CompositeIndexDocumentState[] Documents, CompositeIndexMembership[] Memberships, CompositePhysicalIndexImage[] IndexImages);

internal sealed record CompositePhysicalIndexImage(string PartitionKey, string Space, string IndexName,
    string[] KeyHex, string[] DocumentIds, string[] AfterFourIds);
