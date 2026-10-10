using System.Collections.Immutable;

namespace KeyLoad.Server.Features.Search;

internal static class OnlineTextMcpCatalog
{
    internal static ImmutableArray<McpOperationDescriptor> Entries { get; } =
    [
        McpOperationFactory.Command<OnlineTextIndexMaintenanceRequest, OnlineTextIndexMaintenanceResult>(
            OnlineTextIndexMaintenanceProtocol.Tool, OnlineTextIndexMaintenanceProtocol.Route,
            OperationKind.MaintainOnlineTextIndex, static request => request.CommandId)
    ];
}
