namespace KeyLoad;

internal static class PartitionQueryContractAliases
{
    internal const string Request = "keyload.contract.partition-query-request.v1";
    internal const string Row = "keyload.contract.partition-query-row.v1";
    internal const string LeafWitness = "keyload.contract.partition-query-leaf-witness.v1";
    internal const string Page = "keyload.contract.partition-query-page.v1";
}

internal static class PartitionQueryRequestFieldIds
{
    internal const uint Version = 0;
    internal const uint Partitions = 1;
    internal const uint Query = 2;
    internal const uint Parameters = 3;
    internal const uint AllowFullScan = 4;
    internal const uint AstVersion = 5;
}

internal static class PartitionQueryRowFieldIds
{
    internal const uint Reference = 0;
    internal const uint Row = 1;
}

internal static class PartitionQueryLeafWitnessFieldIds
{
    internal const uint Partition = 0;
    internal const uint CutPosition = 1;
    internal const uint PolicyEpoch = 2;
    internal const uint SchemaVersion = 3;
    internal const uint AccessPath = 4;
}

internal static class PartitionQueryPageFieldIds
{
    internal const uint Version = 0;
    internal const uint Rows = 1;
    internal const uint Leaves = 2;
    internal const uint Complete = 3;
}
