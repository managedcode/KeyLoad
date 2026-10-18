namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal sealed record CompositeIndexDocumentState(string TenantId, string DatabaseId, string TransactionDomainId,
    string PartitionKey, string Collection, string Id, string? Json, long Revision, bool Deleted);
