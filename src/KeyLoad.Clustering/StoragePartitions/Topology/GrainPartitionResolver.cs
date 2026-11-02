using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Maps a command envelope to the ordering key of its atomic partition, catalog or authorization records.</summary>
internal static class GrainPartitionResolver
{
    internal static string Resolve(DecodedGrainRequest request)
    {
        if (request.Envelope.CommandKind is not { } kind || !DatabaseCommandInputTypes.TryFind(kind, out _))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var /* KLD0041: Read the current canonical command input slot only to select its closed operation kind and atomic partition route. */ input = kind is OperationKind.Bm25CorpusPublicationPhase or OperationKind.OnlineTextPublicationPhase
            ? ((GrainPayload.Read<ManagedCode.Communication.Commands.Command<DatabaseCommand>>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as ManagedCode.Communication.Commands.Command<KeyLoad.DatabaseCommand> at this closed operation boundary before executing typed code. */).Value!.Input /* KLD0041: Read the current canonical DatabaseCommand.Input slot of this validated publication command only to select its atomic partition route. */)
            : (GrainPayload.ReadCommandInput(kind, (request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as the selected canonical command input at this closed operation boundary before executing typed code. */);
        var target = (CommandPartitions.Of(kind, input) /* KLD0041: Read the current canonical command input slot only to select its closed operation kind and atomic partition route. */);
        return target.Route switch
        {
            CommandRoute.Partition => Partition(request, target),
            CommandRoute.Authorization => GrainRoutingProtocol.AuthorizationPartition,
            _ => GrainRoutingProtocol.CatalogPartition
        };
    }

    private static string Partition(DecodedGrainRequest request, CommandPartition target)
    {
        if (target.CommandId is { } commandId && commandId != request.Envelope.CommandId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var partition = target.Partition;
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: BCL null validation accepts the partition PartitionRef reference through its object parameter. */;
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
        return partition.AtomicPartitionId;
    }
}
