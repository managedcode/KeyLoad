using System.Collections.Immutable;

namespace KeyLoad.Server;

/// <summary>Explicit typed bindings for the frozen atomic, queue and administration commands.</summary>
internal static class McpCommandCatalog
{
    internal static ImmutableArray<McpOperationDescriptor> Entries
    {
        get;
    } =
    [
        McpOperationFactory.Command<UpsertRemoteGraphEdgeRequestV1, RemoteGraphEdgeResultV1>(RemoteGraphPublicProtocol.UpsertTool,
            RemoteGraphPublicProtocol.UpsertRoute, OperationKind.UpsertRemoteGraphEdge, static request => request.CommandId),
        McpOperationFactory.Command<DeleteRemoteGraphEdgeRequestV1, RemoteGraphEdgeResultV1>(RemoteGraphPublicProtocol.DeleteTool,
            RemoteGraphPublicProtocol.DeleteRoute, OperationKind.DeleteRemoteGraphEdge, static request => request.CommandId),
        McpOperationFactory.Command<DeliverRemoteGraphEdgeRequestV1, RemoteGraphEdgeResultV1>(RemoteGraphPublicProtocol.DeliverTool,
            RemoteGraphPublicProtocol.DeliverRoute, OperationKind.DeliverRemoteGraphEdge, static request => request.CommandId),
        McpOperationFactory.Command<CommandRequest, CommitReceipt>(McpToolNames.DocumentsCommit, McpToolRoutes.DocumentsCommit, OperationKind.Batch, static request => request.CommandId),
        McpOperationFactory.Command<AnnMaintenanceRequest, AnnMaintenanceResult>(McpToolNames.SearchAnnMaintain, McpToolRoutes.SearchAnnMaintain, OperationKind.MaintainAnnIndex, static request => request.CommandId),
        McpOperationFactory.Command<TextIndexMaintenanceRequest, TextIndexMaintenanceResult>(McpToolNames.SearchTextMaintain, McpToolRoutes.SearchTextMaintain, OperationKind.MaintainTextIndex, static request => request.CommandId),
        McpOperationFactory.Command<MultiLaneReceiveRequest, MultiLaneReceiveResult>(McpToolNames.MessagesReceiveAcrossLanes, McpToolRoutes.MessagesReceiveAcrossLanes, OperationKind.ReceiveAcrossLanes, static request => request.RequestId),
        McpOperationFactory.Command<ReceiveRequest, ReceiveResult>(McpToolNames.MessagesReceive, McpToolRoutes.MessagesReceive, OperationKind.Receive, static request => request.RequestId),
        McpOperationFactory.Command<DeliveryCommand, CommitReceipt>(McpToolNames.MessagesComplete, McpToolRoutes.MessagesComplete, OperationKind.Delivery, static request => request.CommandId),
        McpOperationFactory.Command<CommitInboxRequest, CommitInboxResult>(McpToolNames.InboxCommit, McpToolRoutes.InboxCommit, OperationKind.CommitInbox, static request => request.CommandId),
        McpOperationFactory.Command<ProcessingRequest, CommitReceipt>(McpToolNames.MessagesProcess, McpToolRoutes.MessagesProcess, OperationKind.Processing, static request => request.CommandId),
        McpOperationFactory.HeaderCommand<QueueDispatchControlRequest, ResourceDefinition>(
            QueueDispatchControlProtocol.Tool, QueueDispatchControlProtocol.Route, OperationKind.SetQueueDispatch),
        McpOperationFactory.HeaderCommand<ConfigureResourceRequest, ResourceDefinition>(McpToolNames.ResourcesConfigure, McpToolRoutes.ResourcesConfigure, OperationKind.ConfigureResource),
        McpOperationFactory.HeaderCommand<ConfigurePrincipalRequest, PrincipalRecord>(McpToolNames.PrincipalsConfigure, McpToolRoutes.PrincipalsConfigure, OperationKind.ConfigurePrincipal),
        McpOperationFactory.HeaderStatusCommand<ConfigureApiKeyRequest>(McpToolNames.CredentialsConfigure, McpToolRoutes.CredentialsConfigure, OperationKind.ConfigureApiKey),
        McpOperationFactory.HeaderCommand<bool, bool>(McpToolNames.AdminDispatch, McpToolRoutes.AdminDispatch, OperationKind.SetDispatch),
        McpOperationFactory.Command<PartitionMoveRequest, PartitionMoveResult>(McpToolNames.AdminPartitionMove,
            McpToolRoutes.AdminPartitionMove, OperationKind.MovePartition, static request => request.MoveId),
        McpOperationFactory.HeaderCommand<BindAtomicPartitionPlacementRequest, bool>(McpToolNames.AdminPartitionPlacementBind,
            McpToolRoutes.AdminPartitionPlacementBind, OperationKind.BindAtomicPartitionPlacement)
    ];
}
