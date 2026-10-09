namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Independent accepted public movement identity and complete request/result field inventory.</summary>
internal static class McpMovementCatalogProtocol
{
    internal const string Name = "keyload_admin_partition_move";
    internal const string Route = "/v1/admin/partitions/move";
    internal const string Request = "request";
    internal const string MoveId = "moveId";
    internal const string Partition = "partition";
    internal const string Destination = "destinationPhysicalShardId";
    internal const string Revision = "expectedPlacementRevision";
    internal const string Mode = "mode";
    internal const string SourceOwner = "sourceOwner";
    internal const string DestinationOwner = "destinationOwner";
    internal const string Receipt = "installedReceipt";
    internal const string Placement = "publishedPlacement";
    internal const string Properties = "properties";
    internal const string Required = "required";
    internal const string Closed = "additionalProperties";
    internal const string Object = "object";
    internal const string Phase = "phase";
    internal const string Cut = "sourceCut";
    internal const string Minimum = "minimum";
    internal const string Maximum = "maximum";
    internal static readonly string[] Modes = ["None", "Transfer", "Resume", "Abort"];
    internal static readonly string[] Phases = ["None", "Prepared", "Fenced", "Captured", "Transferring", "Installed", "Published", "Retired", "Aborted", "Aborting"];
    internal static readonly string[] RequestFields = [MoveId, Partition, Destination, Revision, Mode];
    internal static readonly string[] ResultFields = [MoveId, Partition, Phase, SourceOwner, DestinationOwner, Cut, Receipt, Placement];
    internal static readonly string[] PartitionFields = ["tenantId", "databaseId", "transactionDomainId", "partitionKey"];
    internal const string Atomic = "atomicPartitionId";
    internal static readonly string[] OwnerFields = ["physicalShardId", "incarnation", "voterIds", "placementEpoch"];
    internal static readonly string[] ReceiptFields = ["incarnation", Atomic, "position", "ownershipEpoch"];
    internal static readonly string[] PlacementFields = ["version", Partition, "physicalShardId", "revision", "incarnation", "voterIds", "placementEpoch"];
}
