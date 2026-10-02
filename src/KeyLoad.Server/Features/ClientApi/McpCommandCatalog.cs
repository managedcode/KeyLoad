using System.Collections.Immutable;

namespace KeyLoad.Server;

/// <summary>Explicit typed bindings for the frozen atomic, queue and administration commands.</summary>
internal static class McpCommandCatalog
{
    internal static ImmutableArray<McpOperationDescriptor> Entries { get; } =
    [
        McpOperationFactory.Command<CommandRequest, CommitReceipt>(McpToolNames.DocumentsCommit, McpToolRoutes.DocumentsCommit, OperationKind.Batch, static request => request.CommandId),
        McpOperationFactory.Command<ReceiveRequest, ReceiveResult>(McpToolNames.MessagesReceive, McpToolRoutes.MessagesReceive, OperationKind.Receive, static request => request.RequestId),
        McpOperationFactory.Command<DeliveryCommand, CommitReceipt>(McpToolNames.MessagesComplete, McpToolRoutes.MessagesComplete, OperationKind.Delivery, static request => request.CommandId),
        McpOperationFactory.Command<ProcessingRequest, CommitReceipt>(McpToolNames.MessagesProcess, McpToolRoutes.MessagesProcess, OperationKind.Processing, static request => request.CommandId),
        McpOperationFactory.HeaderCommand<ConfigureResourceRequest, ResourceDefinition>(McpToolNames.ResourcesConfigure, McpToolRoutes.ResourcesConfigure, OperationKind.ConfigureResource),
        McpOperationFactory.HeaderCommand<ConfigurePrincipalRequest, PrincipalRecord>(McpToolNames.PrincipalsConfigure, McpToolRoutes.PrincipalsConfigure, OperationKind.ConfigurePrincipal),
        McpOperationFactory.HeaderCommand<ConfigureApiKeyRequest, bool>(McpToolNames.CredentialsConfigure, McpToolRoutes.CredentialsConfigure, OperationKind.ConfigureApiKey),
        McpOperationFactory.HeaderCommand<bool, bool>(McpToolNames.AdminDispatch, McpToolRoutes.AdminDispatch, OperationKind.SetDispatch)
    ];
}
