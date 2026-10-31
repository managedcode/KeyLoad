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
        var input = kind is OperationKind.Bm25CorpusPublicationPhase or OperationKind.OnlineTextPublicationPhase
            ? GrainPayload.Read<ManagedCode.Communication.Commands.Command<DatabaseCommand>>(request.Payload).Value!.Input
            : GrainPayload.ReadCommandInput(kind, request.Payload);
        var target = CommandPartitions.Of(kind, input);
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
        ArgumentNullException.ThrowIfNull(partition);
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
        return partition.AtomicPartitionId;
    }
}
