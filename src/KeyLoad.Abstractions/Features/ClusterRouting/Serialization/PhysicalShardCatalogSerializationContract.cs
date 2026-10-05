namespace KeyLoad;

internal static class PhysicalShardCatalogAliases
{
    internal const string Catalog = "keyload.contract.physical-shard-catalog.v1";
    internal const string Record = "keyload.contract.physical-shard-record.v1";
    internal const string BootstrapRequest = "keyload.contract.physical-shard-bootstrap-request.v1";
}

internal static class PhysicalShardCatalogFieldIds
{
    internal const uint Version = 0;
    internal const uint Revision = 1;
    internal const uint DefaultShard = 2;
}

internal static class PhysicalShardRecordFieldIds
{
    internal const uint PhysicalShardId = 0;
    internal const uint Incarnation = 1;
    internal const uint VoterIds = 2;
    internal const uint PlacementEpoch = 3;
}

internal static class PhysicalShardBootstrapRequestFieldIds
{
    internal const uint Version = 0;
    internal const uint ExpectedRevision = 1;
    internal const uint PhysicalShardId = 2;
    internal const uint Incarnation = 3;
    internal const uint VoterIds = 4;
}
