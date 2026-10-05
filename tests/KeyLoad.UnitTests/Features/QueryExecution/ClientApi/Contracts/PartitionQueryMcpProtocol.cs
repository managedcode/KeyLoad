namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class PartitionQueryMcpProtocol
{
    internal const string Tool = "keyload_query_partitions";
    internal const string Route = "/v1/query/partitions";
    internal const string RequestAlias = "keyload.contract.partition-query-request.v1";
    internal const string RowAlias = "keyload.contract.partition-query-row.v1";
    internal const string WitnessAlias = "keyload.contract.partition-query-leaf-witness.v1";
    internal const string PageAlias = "keyload.contract.partition-query-page.v1";
    internal const string Version = "version";
    internal const string Partitions = "partitions";
    internal const string Query = "query";
    internal const string Parameters = "parameters";
    internal const string AllowFullScan = "allowFullScan";
    internal const string AstVersion = "astVersion";
    internal const string Rows = "rows";
    internal const string Leaves = "leaves";
    internal const string Partition = "partition";
    internal const string CutPosition = "cutPosition";
    internal const string PolicyEpoch = "policyEpoch";
    internal const string SchemaVersion = "schemaVersion";
    internal const string Complete = "complete";
    internal const string ObjectType = "object";
    internal const string BooleanType = "boolean";
    internal const string Collection = "collection";
    internal const string Alias = "alias";
    internal const string Projection = "projection";
    internal const string Filter = "filter";
    internal const string Order = "order";
    internal const string Limit = "limit";
    internal const string Explain = "explain";
    internal const string ModelSource = "modelSource";
    internal const string AdditionalProperties = "additionalProperties";
    internal const string Reference = "reference";
    internal const string Row = "row";
    internal const string EntityId = "entityId";
    internal const string Revision = "revision";
    internal const string Json = "json";
    internal const string Redacted = "redacted";
    internal const string RedactedFields = "redactedFields";
    internal const string AccessPath = "accessPath";
    internal const string Id = "id";
    internal const string TenantId = "tenantId";
    internal const string DatabaseId = "databaseId";
    internal const string TransactionDomainId = "transactionDomainId";
    internal const string PartitionKey = "partitionKey";
    internal const string AtomicPartitionId = "atomicPartitionId";
}
